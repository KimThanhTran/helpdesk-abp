"""End-to-end smoke test against a running Helpdesk API (real HTTP, real PostgreSQL, real tokens).

    python scripts/smoke_test.py http://localhost:8080

Needs the demo data (agent1, alice, bob, admin; password 1q2w3E*) and the "Helpdesk_Demo" client,
which exist when the stack runs with Helpdesk__SeedDemoData=true. Uses only the standard library.
Exits with a non-zero status if any check fails.

If SHEETS_DUMP_URL is set (the URL of integrations/google-sheets/local-server.js /dump, with the API pointed at
it and IncludeTitle/IncludePeople on), it also checks that the ticket reached the Google Sheet intact.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8080").rstrip("/")
PASSWORD = "1q2w3E*"  # the documented demo password
NEW, OPEN, WAITING, RESOLVED, CLOSED, CANCELLED = 1, 2, 3, 4, 5, 6

failures = []
checks = 0


def call(method, path, token=None, body=None, form=None):
    """Returns (status, parsed json or None)."""
    data = None
    headers = {}
    if form is not None:
        data = urllib.parse.urlencode(form).encode()
        headers["Content-Type"] = "application/x-www-form-urlencoded"
    elif body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as error:
        raw = error.read()
        try:
            return error.code, json.loads(raw) if raw else None
        except ValueError:
            return error.code, None


def check(name, condition, detail=""):
    global checks
    checks += 1
    print(f"  {'PASS' if condition else 'FAIL'}  {name}" + (f"   [{detail}]" if not condition and detail else ""))
    if not condition:
        failures.append(name)


def expect(name, response, status):
    check(f"{name} -> {status}", response[0] == status, f"got {response[0]}: {json.dumps(response[1])[:200]}")
    return response[1]


def wait_until_ready():
    for _ in range(90):
        try:
            if call("GET", "/api/abp/application-configuration")[0] == 200:
                return
        except Exception:
            pass
        time.sleep(2)
    sys.exit(f"The API at {BASE} did not become ready.")


def token(user):
    status, body = call("POST", "/connect/token", form={
        "grant_type": "password", "client_id": "Helpdesk_Demo", "username": user,
        "password": PASSWORD, "scope": "Helpdesk",
    })
    if status != 200:
        sys.exit(f"Could not sign in as {user} ({status}): {body}")
    return body["access_token"]


def check_sheet(dump_url, code):
    """Waits for the asynchronous export to settle, then checks the final state of the ticket in the sheet."""
    ticket_row, event_rows = None, []
    for _ in range(30):
        with urllib.request.urlopen(dump_url, timeout=10) as response:
            sheets = json.load(response).get("sheets", {})
        tickets = [r for r in sheets.get("Tickets", [])[1:] if r[0] == code]
        ticket_row = tickets[0] if tickets else None
        event_rows = [r for r in sheets.get("Events", [])[1:] if r[1] == code]
        if ticket_row and ticket_row[3] == "Closed":
            break
        time.sleep(0.5)

    check("the ticket's row shows its FINAL state (Closed), not an earlier one",
          bool(ticket_row) and ticket_row[3] == "Closed", str(ticket_row and ticket_row[3]))
    check("the title and the people are there because this stack turns them on",
          bool(ticket_row) and ticket_row[1] == "Printer jams" and ticket_row[5] == "alice" and ticket_row[6] == "agent1")
    names = [r[2] for r in event_rows]
    statuses = [r[3] for r in event_rows]
    check("the log starts with the creation of the ticket, which is easy to lose", names[:1] == ["Created"] and statuses[:1] == ["New"], str(names))
    check("each logged status is the one AFTER the change, ending in Closed", statuses[-1:] == ["Closed"] and "Resolved" in statuses, str(statuses))
    check("one ticket row and no duplicates", len({r[4] for r in event_rows}) == len(event_rows))


def main():
    print(f"Smoke test against {BASE}")
    wait_until_ready()
    alice, bob, agent, admin = (token(u) for u in ("alice", "bob", "agent1", "admin"))

    print("\nSigned out and role checks")
    expect("anonymous cannot list categories", call("GET", "/api/app/category"), 401)
    expect("requester cannot open the agent queue", call("GET", "/api/app/ticket/queue", alice), 403)
    expect("requester cannot read reports", call("GET", "/api/app/ticket-report/summary", alice), 403)

    print("\nCategories")
    categories = expect("requester lists categories", call("GET", "/api/app/category", alice), 200)["items"]
    it = next(c["id"] for c in categories if c["name"] == "IT support")
    expect("requester cannot create a category", call("POST", "/api/app/category", alice, {"name": "Sneaky"}), 403)
    expect("admin creates a category", call("POST", "/api/app/category", admin, {"name": "Smoke " + str(int(time.time()))}), 200)
    dup = expect("duplicate category name is a conflict", call("POST", "/api/app/category", admin, {"name": "it SUPPORT"}), 409)
    check("conflict carries the business code", (dup or {}).get("error", {}).get("code") == "Helpdesk:Category:DuplicateName")

    print("\nCreating tickets")
    body = {"categoryId": it, "title": "Printer jams", "description": "Page two always jams"}
    ticket = expect("alice opens a ticket", call("POST", "/api/app/ticket", alice, body), 200)
    tid = ticket["id"]
    check("ticket is new, numbered HD-yyyy-nnnnnn and unassigned",
          ticket["status"] == NEW and ticket["code"].startswith("HD-") and ticket["assignee"] is None, ticket["code"])
    expect("empty title is a validation error", call("POST", "/api/app/ticket", alice, {**body, "title": ""}), 400)
    expect("unknown category is not found", call("POST", "/api/app/ticket", alice, {**body, "categoryId": "00000000-0000-0000-0000-000000000001"}), 404)

    print("\nWho may see and do what")
    expect("bob cannot see alice's ticket (404, not 403)", call("GET", f"/api/app/ticket/{tid}", bob), 404)
    expect("requester cannot claim", call("POST", f"/api/app/ticket/{tid}/claim", alice), 403)
    expect("agent cannot confirm", call("POST", f"/api/app/ticket/{tid}/confirm", agent), 403)
    conflict = expect("resolving a new ticket is a conflict", call("POST", f"/api/app/ticket/{tid}/resolve", agent, {"resolution": "Done"}), 409)
    check("conflict carries the business code", (conflict or {}).get("error", {}).get("code") == "Helpdesk:Ticket:InvalidTransition")

    print("\nWorking the ticket")
    claimed = expect("agent claims it", call("POST", f"/api/app/ticket/{tid}/claim", agent), 200)
    check("ticket is open and assigned to agent1", claimed["status"] == OPEN and claimed["assignee"]["name"] == "agent1")
    expect("an agent comment", call("POST", f"/api/app/ticket/{tid}/comment", agent, {"body": "Which model?"}), 200)
    expect("an internal note", call("POST", f"/api/app/ticket/{tid}/comment", agent, {"body": "SECRET-NOTE", "visibility": 2}), 200)
    expect("requester cannot write an internal note",
           call("POST", f"/api/app/ticket/{tid}/comment", alice, {"body": "x", "visibility": 2}), 403)

    seen_by_alice = expect("alice reads her ticket", call("GET", f"/api/app/ticket/{tid}", alice), 200)
    seen_by_agent = expect("agent reads the ticket", call("GET", f"/api/app/ticket/{tid}", agent), 200)
    check("requester sees one comment and no internal note",
          len(seen_by_alice["comments"]) == 1 and "SECRET-NOTE" not in json.dumps(seen_by_alice))
    check("agent sees both comments", len(seen_by_agent["comments"]) == 2)
    check("requester's actions never include the internal note (12 = cancel is gone, 2 = internal note)",
          2 not in seen_by_alice["actions"])

    waiting = expect("agent waits for the requester", call("POST", f"/api/app/ticket/{tid}/wait", agent, {"reason": "Need the model"}), 200)
    check("ticket is waiting", waiting["status"] == WAITING)
    expect("requester replies", call("POST", f"/api/app/ticket/{tid}/comment", alice, {"body": "LaserJet 400"}), 200)
    reopened = expect("ticket is back to open after the reply", call("GET", f"/api/app/ticket/{tid}", alice), 200)
    check("status is open again", reopened["status"] == OPEN)

    print("\nResolving and closing")
    expect("agent resolves", call("POST", f"/api/app/ticket/{tid}/resolve", agent, {"resolution": "Replaced the roller"}), 200)
    closed = expect("requester confirms", call("POST", f"/api/app/ticket/{tid}/confirm", alice), 200)
    check("ticket is closed by the requester", closed["status"] == CLOSED and closed["closeReason"] == 1)
    expect("comments on a closed ticket are a conflict", call("POST", f"/api/app/ticket/{tid}/comment", alice, {"body": "More"}), 409)
    check("history tells the story", [e["toStatus"] for e in closed["events"]] == [NEW, OPEN, WAITING, OPEN, RESOLVED, CLOSED],
          str([e["toStatus"] for e in closed["events"]]))

    print("\nLists and reports")
    mine = expect("alice's tickets", call("GET", "/api/app/ticket/my-list", alice), 200)
    check("only her own tickets", all(t["requester"]["name"] == "alice" for t in mine["items"]) and mine["totalCount"] >= 1)
    queue = expect("agent's queue (all)", call("GET", "/api/app/ticket/queue?Scope=3&MaxResultCount=50", agent), 200)
    check("the agent sees tickets from several people", queue["totalCount"] >= 1)
    expect("agent lists assignable agents", call("GET", "/api/app/ticket/assignable-agents", agent), 200)
    summary = expect("agent reads the summary", call("GET", "/api/app/ticket-report/summary", agent), 200)
    check("summary counts the tickets", summary["total"] >= 1)

    dump_url = os.environ.get("SHEETS_DUMP_URL")
    if dump_url:
        print("\nGoogle Sheets export")
        check_sheet(dump_url, closed["code"])

    print(f"\n{checks - len(failures)}/{checks} checks passed")
    if failures:
        print("Failed:", *failures, sep="\n  - ")
        sys.exit(1)


if __name__ == "__main__":
    main()
