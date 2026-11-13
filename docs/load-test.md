# Load test

`scripts/load/run.sh` starts the stack from empty, adds 2,000 tickets (`scripts/load/seed.sql`) and runs
[k6](https://k6.io) (`scripts/load/load.js`, in Docker): requesters list their tickets, open one and now and then create
one; agents open the queue, search it, read the report, ask for suggestions and now and then export to Excel. Every route
has its own time limit (95 calls in 100 must answer faster), and the run fails if one is crossed.

```bash
scripts/load/run.sh                              # 10 requesters + 10 agents for 30 s
REQUESTERS=100 AGENTS=100 scripts/load/run.sh    # 200 people at once
```

## Results

One laptop (16 logical processors, Docker Desktop), the API in one container with PostgreSQL 16 beside it. Times are
averages, with the 95th percentile in brackets. These numbers say how the code behaves on that machine; they do not
promise anything about yours.

| | 20 people at once | 200 people, before the fixes below | 200 people, now |
| --- | ---: | ---: | ---: |
| Requests per second | 55 | 236 | 437 |
| Open the queue | 26 ms (23) | 290 ms (1,080) | 112 ms (269) |
| My tickets | 26 ms (26) | 274 ms (681) | 103 ms (262) |
| Open a ticket | 19 ms (19) | 217 ms (383) | 79 ms (250) |
| The report | 19 ms (22) | 245 ms (433) | 82 ms (238) |
| Suggestions | 13 ms (14) | 333 ms (518) | 48 ms (172) |
| Excel export | 53 ms (86) | 351 ms (506) | 167 ms (309) |
| **Create a ticket** | 21 ms (28) | **8,210 ms (23,330)** | **286 ms (1,000)** |
| Failed requests | 0 | 0 | 0 |

## What it found

Runs vary: an earlier run of the code before any change, on a database that was not freshly seeded, gave 3.4 s on average
for a create and 340 requests per second. The direction is the same every time; the exact numbers move with whatever else
the machine is doing.

At 200 people creating a ticket took 8 seconds on average and up to 29. Reading was fine, so something specific was wrong.
Two measurements showed what, instead of a guess:

- **The API was using about 1,240 % of a processor** (12 of 16 cores). The ticket suggestions rebuilt their search index
  from the newest 2,000 tickets on every request. That was the comment "rebuilt per request, fine up to a few thousand
  tickets" in the code, and it was not fine. The index is now kept for 15 seconds (`Helpdesk:TriageCacheSeconds`), one
  caller builds it while the others wait for the same result, and a failed build is not kept. The cost: a new ticket
  appears in the suggestions up to 15 seconds later.
- **49 database connections were waiting on one row**: the ticket-number counter. A number is a row lock held until the
  commit, on purpose: if a transaction is lost the number is not skipped. That makes creating tickets a queue, and with
  the processor exhausted the transaction holding the lock was slow to finish, so everyone behind it waited. Creating now
  reads everything the answer needs *before* taking the number, so only two statements run while the lock is held.

The number lock is still the limit for creating tickets. At 200 people the 95th percentile of a create is 1 second, over
the 500 ms the test asks for, so that threshold is crossed in the 200-person run (the other seven routes pass, and so do the error-rate and check thresholds). Giving up
gap-free numbers (a database sequence does not wait for anyone) would remove the queue; it is a decision about the
product, not a tuning knob, and it has not been taken.

PostgreSQL's default of 100 connections is also the default size of the API's own pool, so one API instance could use
every slot and even `psql` was refused ("too many clients"). The compose file now allows 300 and gives the API 150. This
did not change the timings above; it only keeps the database reachable under load.

## What this does not cover

- One API instance. Several instances would share the database and the number lock, and each would keep its own copy of
  the suggestions index.
- A database that fits in memory, 2,000 tickets and about 2,000 more created during a run. Nothing here says how it
  behaves with a million tickets (the list queries are indexed on status and requester; the text search is not).
- SignalR connections: the test does not open any, so the cost of 200 live notification connections is unmeasured.
- Machines other than this one, and a network between the browser and the API.
