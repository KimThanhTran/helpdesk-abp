-- 2,000 extra closed tickets with varied text, copied from a real closed one, so the lists, the report and the
-- triage index have a realistic amount to work on.
DO $$
DECLARE
  subjects text[] := ARRAY['VPN','printer','email','laptop','Wi-Fi','password','badge','monitor','keyboard','projector','payslip','contract','chair','air conditioner','door lock','elevator','phone','badge reader','server','backup'];
  problems text[] := ARRAY['disconnects often','jams','will not sync','is very slow','keeps dropping','expired','does not scan','flickers','is broken','shows an error','is missing','needs renewal','leaks water','is stuck','makes noise','cannot open','is out of paper','lost the connection','crashes','is not working'];
  places text[] := ARRAY['floor 2','floor 3','meeting room A','meeting room B','the open space','the warehouse','reception','the lab','the data centre','my desk'];
  i int;
BEGIN
  CREATE TEMP TABLE one AS SELECT * FROM "AppTickets" WHERE "Status" = 5 AND "Resolution" IS NOT NULL LIMIT 1;
  FOR i IN 1..2000 LOOP
    UPDATE one SET
      "Id" = gen_random_uuid(),
      "Code" = 'LT-' || lpad(i::text, 6, '0'),
      "Title" = subjects[1 + (i % 20)] || ' ' || problems[1 + ((i * 7) % 20)] || ' in ' || places[1 + ((i * 3) % 10)],
      "Description" = 'The ' || subjects[1 + (i % 20)] || ' ' || problems[1 + ((i * 7) % 20)] || ' near ' || places[1 + ((i * 3) % 10)] || '. It started yesterday and affects several people.',
      "Resolution" = 'Checked the ' || subjects[1 + (i % 20)] || ' and fixed it.';
    INSERT INTO "AppTickets" SELECT * FROM one;
  END LOOP;
  DROP TABLE one;
END $$;
SELECT count(*) AS tickets FROM "AppTickets";
