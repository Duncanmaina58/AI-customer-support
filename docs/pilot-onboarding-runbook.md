# Pilot Onboarding Runbook — Sprint 8

**Checklist items this supports:**
- "5 pilot clients onboarded end-to-end through wizard personally"
- "Each pilot client has at least 10 live conversations with the AI"
- "Feedback collected from each pilot client — documented"

These three are operational/business activities — onboarding a real client is
a conversation with a real person, not something to automate away. This
runbook exists so that work is *consistent* and *documented* across all 5
pilots, and gives you a concrete script and a feedback template rather than a
blank page each time.

## Before pilot #1

- [ ] Run through `docs/security-audit.md`'s "Recommendations" section —
      especially setting `Meta:AppSecret` if any pilot will connect
      WhatsApp/Messenger, since that's now required for those webhooks to work
      at all (they fail closed once the secret is set, by design).
- [ ] Run the load test (`docs/load-testing.md`) against the environment
      pilots will actually use.
- [ ] Pick (or create) one throwaway/test company to rehearse the whole
      wizard yourself before the first real pilot call, so you're not
      debugging the onboarding flow live with a client watching.

## Per-pilot checklist

Copy this section per client (a new heading per pilot works well in whatever
you track this in — a shared doc, Notion, Linear, etc. — this file just
defines *what* to capture, not where).

### Pilot: `<company name>`

- **Kickoff date:**
- **Primary contact:**
- **Industry / use case:**

**Wizard walkthrough (do this live with them, screen-share):**
- [ ] Step 1 — Company details entered accurately (name, KES/currency if
      relevant, timezone)
- [ ] Step 2 — Brand voice set to match how *they* actually talk to
      customers, not a generic default
- [ ] Step 3 — Real business hours entered (this drives after-hours
      escalation behaviour — get it right)
- [ ] Step 4 — At least one real channel connected (WhatsApp / Messenger /
      Telegram / Email / Web Chat) — not left in a half-configured state
- [ ] Step 5 — Escalation rules reviewed with them — ask what topics they
      *never* want the AI answering alone (pricing disputes, refunds, medical/
      legal-adjacent questions are common ones) and configure accordingly
- [ ] Step 6 — Test conversation run together during the call, both of you
      watching the response
- [ ] Knowledge base seeded — at minimum their FAQ/pricing/policies as manual
      entries, or their live website connected via Web Sources
      (`docs/migrations/sprint4-web-crawling.sql` covers what that adds) if
      they have one

**Go-live:**
- [ ] Widget/channel actually live and reachable by real customers (not just
      the sandbox)
- [ ] You've personally sent >= 1 test message through the *live* channel
      end-to-end after go-live, not just in the wizard's test step

**10 live conversations:**
- [ ] Conversation count checked in Analytics >= 10 real (non-sandbox,
      non-test) conversations — note the date this threshold was hit:
      `______`

**Feedback (collect after they've had real usage, not just at kickoff):**
- Date collected:
- What's working well:
- What's confusing / broken / missing:
- Would they recommend it to another business like theirs? (quick NPS-style
  gut check, 0–10):
- Any escalations/tickets that reveal a knowledge-base gap or a bad AI
  response — link the specific conversation:
- Follow-up actions taken as a result:

---

## After all 5 pilots

- [ ] Read back across all 5 feedback sections above — look for the *same*
      complaint showing up more than once; that's a product priority, not a
      one-off.
- [ ] Any conversations flagged as "bad AI response" are worth a manual
      review pass — check whether it's a knowledge-base gap (add the missing
      content), a prompt/brand-voice issue, or an escalation-rule gap (add a
      rule so it hands off instead of guessing next time).
