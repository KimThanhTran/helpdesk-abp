"""End-to-end smoke test against a running Helpdesk API (real HTTP, real PostgreSQL, real tokens).

    python scripts/smoke_test.py http://localhost:8080

Needs the demo data (agent1, alice, bob, admin; password 1q2w3E*) and the "Helpdesk_Demo" client,
which exist when the stack runs with Helpdesk__SeedDemoData=true. Uses only the standard library.
Exits with a non-zero status if any check fails.

If SHEETS_DUMP_URL is set (the URL of integrations/google-sheets/local-server.js /dump, with the API pointed at
it and IncludeTitle/IncludePeople on), it also checks that the ticket reached the Google Sheet intact.
"""
import io
import json
import os
import struct
import sys
import time
import zipfile
import zlib
import urllib.error
import urllib.parse
import urllib.request

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8080").rstrip("/")
PASSWORD = "1q2w3E*"  # the documented demo password
NEW, OPEN, WAITING, RESOLVED, CLOSED, CANCELLED = 1, 2, 3, 4, 5, 6

failures = []
checks = 0


def call(method, path, token=None, body=None, form=None, extra_headers=None):
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
    headers.update(extra_headers or {})
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


def raw(method, path, token=None, data=None, headers=None):
    """Returns (status, response headers, body bytes); for files, where call() would try to parse JSON."""
    sent = {**({"Authorization": f"Bearer {token}"} if token else {}), **(headers or {})}
    request = urllib.request.Request(BASE + path, data=data, headers=sent, method=method)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.status, response.headers, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.headers, error.read()


def upload(ticket_id, token, filename, content, visibility=1):
    """POST /api/app/ticket-attachment/upload/{id} as multipart/form-data; returns (status, parsed json)."""
    boundary = "----smoke" + str(int(time.time() * 1000))
    head = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{filename}"\r\n'
            "Content-Type: image/png\r\n\r\n").encode()
    body = head + content + f"\r\n--{boundary}--\r\n".encode()
    status, _, payload = raw("POST", f"/api/app/ticket-attachment/upload/{ticket_id}?visibility={visibility}", token, body,
                             {"Content-Type": f"multipart/form-data; boundary={boundary}"})
    try:
        return status, json.loads(payload) if payload else None
    except ValueError:
        return status, None


def png_bytes(extra=0):
    """A real 8x8 red PNG; `extra` pads it with an ancillary text chunk to reach a size."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    pixels = b"".join(b"\x00" + b"\xff\x00\x00" * 8 for _ in range(8))
    padding = chunk(b"tEXt", b"pad\x00" + b"x" * extra) if extra else b""
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 2, 0, 0, 0)) + padding
            + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))


def sheet_text(xlsx_bytes, part="xl/worksheets/sheet1.xml"):
    """The XML of one sheet of an .xlsx (the files here use inline strings, so the text is in it)."""
    with zipfile.ZipFile(io.BytesIO(xlsx_bytes)) as archive:
        assert archive.testzip() is None
        return archive.read(part).decode("utf-8")


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
    # a browser that holds an XSRF-TOKEN cookie from another ABP app on localhost must still be able to post
    expect("a stale XSRF-TOKEN cookie does not break a bearer-token POST",
           call("POST", "/api/app/ticket", alice, body, extra_headers={"Cookie": "XSRF-TOKEN=stale"}), 200)
    expect("empty title is a validation error", call("POST", "/api/app/ticket", alice, {**body, "title": ""}), 400)
    expect("unknown category is not found", call("POST", "/api/app/ticket", alice, {**body, "categoryId": "00000000-0000-0000-0000-000000000001"}), 404)

    print("\nWho may see and do what")
    expect("bob cannot see alice's ticket (404, not 403)", call("GET", f"/api/app/ticket/{tid}", bob), 404)
    expect("requester cannot claim", call("POST", f"/api/app/ticket/{tid}/claim", alice), 403)
    status, in_vietnamese = call("POST", f"/api/app/ticket/{tid}/claim", alice, extra_headers={"Accept-Language": "vi"})
    check("the same refusal comes back in Vietnamese when the caller asks for it",
          status == 403 and "Vai trò của bạn" in in_vietnamese["error"]["message"], str(in_vietnamese))
    status, in_english = call("POST", f"/api/app/ticket/{tid}/claim", alice, extra_headers={"Accept-Language": "fr"})
    check("and in English for a language it does not offer", status == 403 and "Your role" in in_english["error"]["message"], str(in_english))
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

    print("\nImage attachments")
    shot = expect("alice opens a ticket for the attachments", call("POST", "/api/app/ticket", alice, body), 200)["id"]
    png = png_bytes()
    status, saved = upload(shot, alice, "screen.png", png)
    check("alice attaches a PNG", status == 200 and saved["contentType"] == "image/png" and saved["size"] == len(png), str(status))
    status, rejected = upload(shot, alice, "photo.png", b"<html><script>alert(1)</script></html>")
    check("a script renamed to .png is refused with 415", status == 415 and rejected["error"]["code"] == "Helpdesk:Attachment:BadType", str(status))
    status, _ = upload(shot, alice, "x.png", png, visibility=2)
    check("a requester cannot add an internal attachment (403)", status == 403, str(status))
    status, internal = upload(shot, agent, "internal.png", png, visibility=2)
    check("the agent adds an internal attachment", status == 200, str(status))
    seen_by_alice = expect("alice lists the attachments", call("GET", f"/api/app/ticket-attachment?ticketId={shot}", alice), 200)["items"]
    seen_by_agent = expect("the agent lists the attachments", call("GET", f"/api/app/ticket-attachment?ticketId={shot}", agent), 200)["items"]
    check("alice sees only the public one, the agent sees both", len(seen_by_alice) == 1 and len(seen_by_agent) == 2, f"{len(seen_by_alice)}/{len(seen_by_agent)}")
    status, headers, content = raw("GET", f"/api/app/ticket-attachment/{saved['id']}/content", alice)
    check("alice downloads the same bytes with the detected type and nosniff",
          status == 200 and content == png and headers["Content-Type"] == "image/png" and headers["X-Content-Type-Options"] == "nosniff", str(status))
    check("the download is an attachment, not rendered inline", "attachment" in (headers["Content-Disposition"] or ""))
    check("alice cannot open the internal file (404)", raw("GET", f"/api/app/ticket-attachment/{internal['id']}/content", alice)[0] == 404)
    check("bob can neither list nor open alice's files (404)",
          raw("GET", f"/api/app/ticket-attachment?ticketId={shot}", bob)[0] == 404
          and raw("GET", f"/api/app/ticket-attachment/{saved['id']}/content", bob)[0] == 404)
    check("anonymous cannot open a file (401)", raw("GET", f"/api/app/ticket-attachment/{saved['id']}/content")[0] == 401)
    for _ in range(3):
        upload(shot, alice, "more.png", png)
    status, limit = upload(shot, alice, "sixth.png", png)
    check("a sixth image is a conflict", status == 409 and limit["error"]["code"] == "Helpdesk:Attachment:Limit", str(status))
    big = expect("alice opens another ticket for the size check", call("POST", "/api/app/ticket", alice, body), 200)["id"]
    status, _ = upload(big, alice, "huge.png", png_bytes(5 * 1024 * 1024))
    check("an image over 5 MB is refused with 413", status == 413, str(status))

    print("\nExcel export")
    status, headers, workbook = raw("GET", "/api/app/ticket-export/tickets?Filter=Printer", agent)
    check("the agent downloads the tickets as .xlsx",
          status == 200 and headers["Content-Type"].startswith("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"), str(status))
    check("it is a valid zip with the ticket in it and no formulas", "Printer jams" in sheet_text(workbook) and "<f>" not in sheet_text(workbook))
    check("the file has a dated name", ".xlsx" in (headers["Content-Disposition"] or ""))
    # the UI on another origin (the Pages site, or the Flutter web build) reads that name; the browser hides it unless exposed
    _, cross, _ = raw("GET", "/api/app/ticket-export/tickets?Filter=Printer", agent, headers={"Origin": "http://localhost:5001"})
    check("a page on an allowed origin may read the download's file name",
          "Content-Disposition" in (cross["Access-Control-Expose-Headers"] or ""), str(cross["Access-Control-Expose-Headers"]))
    _, other, _ = raw("GET", "/api/app/ticket-export/tickets?Filter=Printer", agent, headers={"Origin": "http://evil.example"})
    check("and a page on an origin that is not allowed gets no CORS permission", other["Access-Control-Allow-Origin"] is None)
    empty = raw("GET", "/api/app/ticket-export/tickets?Filter=zzzzzz", agent)[2]
    check("a filter that matches nothing leaves only the header row", "Printer jams" not in sheet_text(empty))
    check("a requester cannot export (403)", raw("GET", "/api/app/ticket-export/tickets", alice)[0] == 403)
    check("anonymous cannot export (401)", raw("GET", "/api/app/ticket-export/tickets")[0] == 401)
    status, headers, report = raw("GET", "/api/app/ticket-export/summary", agent)
    check("the agent downloads the report as .xlsx with four sheets",
          status == 200 and "Tickets created" in sheet_text(report) and "xl/worksheets/sheet4.xml" in zipfile.ZipFile(io.BytesIO(report)).namelist(), str(status))

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
