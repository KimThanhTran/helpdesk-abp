# E-mail

The requester can hear about their ticket by e-mail and answer by replying. It is off until you give it a mail server.

## Mail out

The requester gets a mail when their ticket is **received**, when an agent is **waiting for their answer**, when it is
**resolved**, **closed automatically** or **cancelled** by an agent, and when an agent writes a **public reply**. They never
get a mail about something they did themselves, and **internal notes are never mailed**. The subject carries the ticket code
(`[HD-2026-000042] …`), so replies stay in one thread; the body is plain text, in English or Vietnamese
(`Helpdesk__Email__Language`), with a link to the ticket.

| Setting (environment variable) | Meaning |
| --- | --- |
| `Helpdesk__Email__Host`, `…__Port` | The SMTP server (587 by default). Both mail out and the whole feature stay off while the host or the sender is empty. |
| `Helpdesk__Email__UseSsl` | Encrypt the connection (STARTTLS on 587, TLS on 465). Turn off only for a local test server. |
| `Helpdesk__Email__User`, `…__Password` | SMTP sign-in. Keep the password in an environment variable. |
| `Helpdesk__Email__From` | The sender address, and where replies go. |
| `Helpdesk__Email__PublicUrl` | The address of the web UI, for the link in each mail. |
| `Helpdesk__Email__Language` | `en` or `vi`. A mail is written once, not per person. |
| `Helpdesk__Email__InboundSecret` | Turns on replies (below). |

In `docker-compose.yml` the same settings are `MAIL_HOST`, `MAIL_PORT`, `MAIL_USE_SSL`, `MAIL_USER`, `MAIL_PASSWORD`,
`MAIL_FROM`, `MAIL_PUBLIC_URL`, `MAIL_LANGUAGE` and `MAIL_INBOUND_SECRET`.

### Try it without a real mail server

```bash
MAIL_HOST=mailpit MAIL_PORT=1025 MAIL_USE_SSL=false MAIL_FROM=helpdesk@example.test MAIL_INBOUND_SECRET=dev-inbound-secret \
  docker compose --profile mail-demo up --build
```

Mailpit catches every mail and shows it on <http://localhost:8025>. Sign in as `alice`, open a ticket, then as `agent1` take it,
wait for the requester and resolve it, and watch the mails arrive. `MAIL_DUMP_URL=http://localhost:8025 python scripts/smoke_test.py`
checks all of this, including the reply below.

With Gmail: host `smtp.gmail.com`, port 587, your address as user and sender, and an *app password* (not your normal one) as the password.

## Mail in: replying adds a comment

A reply to a ticket mail becomes a public comment from the requester, through the same service the web page uses, so every rule
applies: a reply on a waiting ticket puts it back to work and restarts its SLA clock, and a reply on a closed ticket is refused.
The quoted message and the signature are cut off.

Mail providers deliver incoming mail by posting it to an address you give them. This API listens on
`POST /api/inbound-email` with `{"from": "...", "subject": "...", "text": "..."}`, and it only accepts a request that is **signed**
(otherwise anyone who found the address could write as any requester):

- `X-Timestamp`: the time in Unix seconds, no more than five minutes from the server's clock;
- `X-Signature`: `sha256=` and the hex HMAC-SHA256 of `"<timestamp>.<body>"` with `Helpdesk__Email__InboundSecret`.

```bash
BODY='{"from":"alice@example.com","subject":"Re: [HD-2026-000001] hi","text":"It is the third floor printer."}'
TS=$(date +%s)
SIG=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | sed 's/^.* //')
curl -i -X POST http://localhost:8080/api/inbound-email -H "Content-Type: application/json" \
  -H "X-Timestamp: $TS" -H "X-Signature: sha256=$SIG" -d "$BODY"
```

It answers **202** when the reply was added, **200** with the reason when it was understood but not used (`NoTicket`, `WrongSender`,
`Empty`, `Refused`), so the provider does not retry it, **401** for a bad or old signature, and **404** while no secret is set.

## What this does not do

- **No real provider has been connected to the webhook.** Mailgun, SendGrid, Postmark and the others post their own formats and
  their own signatures. Between one of them and this address you need a small forwarder (a serverless function, n8n, Zapier)
  that turns the mail into the JSON above and signs it. The signature and the endpoint are tested with requests signed the
  way a forwarder would; nothing else.
- **A From address can be forged.** The reply is used only if the sender is the requester's address, but a forged From is
  indistinguishable here. The provider that receives the mail is expected to reject mail that fails SPF/DKIM before it posts it on.
- **Mail is sent while the request waits**, with a ten-second limit, and there is no queue and no retry: a mail that fails is logged
  and lost, and a very slow mail server slows the call that caused the mail. A queue with retries (an outbox) is the next step.
- **One language per installation** for outgoing mail, and mail goes only to the requester (not to agents or assignees).
- **Plain text only, and an e-mail is not private:** it can show the resolution and an agent's reply to anyone who can read the
  requester's mailbox or the mail in transit. Use TLS (`UseSsl`) and think before enabling it for sensitive tickets.
- Tested against Mailpit, not against Gmail or any other real server.
