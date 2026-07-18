import type { ReactNode } from 'react'
import type { EscalationRules } from '@/lib/types'
import { DEFAULT_ESCALATION_RULES } from '@/lib/types'

/** Parses Company.escalationRulesJson, filling in defaults for any missing field. */
export function parseEscalationRules(json: string | null): EscalationRules {
  if (!json) return DEFAULT_ESCALATION_RULES
  try {
    const parsed = JSON.parse(json) as Partial<EscalationRules>
    return { ...DEFAULT_ESCALATION_RULES, ...parsed }
  } catch {
    // Malformed stored JSON shouldn't crash the wizard or settings page.
    return DEFAULT_ESCALATION_RULES
  }
}

const TEAMS = ['Support', 'Finance', 'IT', 'Logistics', 'Billing']

export function EscalationRulesForm({
  rules,
  onChange,
}: {
  rules: EscalationRules
  onChange: (next: EscalationRules) => void
}) {
  function set<K extends keyof EscalationRules>(key: K, value: EscalationRules[K]) {
    onChange({ ...rules, [key]: value })
  }

  return (
    <div className="space-y-3">
      <RuleRow
        title="Escalate when the AI isn't confident"
        description="If the AI's confidence score falls below the threshold, a ticket is created instead of guessing."
        checked={rules.escalateOnLowConfidence}
        onToggle={(v) => set('escalateOnLowConfidence', v)}
      >
        {rules.escalateOnLowConfidence && (
          <div className="mt-3 flex items-center gap-3">
            <label className="text-xs text-muted-400">Confidence threshold</label>
            <input
              type="range"
              min={0.1}
              max={0.95}
              step={0.05}
              value={rules.confidenceThreshold}
              onChange={(e) => set('confidenceThreshold', Number(e.target.value))}
              className="flex-1 accent-teal-500"
            />
            <span className="w-10 shrink-0 text-right text-xs font-mono text-mint-300">
              {rules.confidenceThreshold.toFixed(2)}
            </span>
          </div>
        )}
      </RuleRow>

      <RuleRow
        title="Escalate when a customer asks for a human"
        description="Phrases like “talk to a person” or “speak to an agent” always create a ticket, regardless of AI confidence."
        checked={rules.escalateOnAgentRequest}
        onToggle={(v) => set('escalateOnAgentRequest', v)}
      />

      <RuleRow
        title="Escalate on payment-related messages"
        description="Mentions of refunds, billing, M-Pesa, or charges are routed to a human — useful if you'd rather not let the AI handle money questions."
        checked={rules.escalateOnPaymentKeywords}
        onToggle={(v) => set('escalateOnPaymentKeywords', v)}
      />

      <div className="rounded-lg border border-ink-700 p-4">
        <label className="mb-1.5 block text-sm text-line-200">Default team for new tickets</label>
        <p className="mb-2 text-xs text-muted-400">
          Tickets are assigned to this team unless a rule sets a different one. You can reassign any ticket later.
        </p>
        <select
          value={rules.defaultAssignedTeam}
          onChange={(e) => set('defaultAssignedTeam', e.target.value)}
          className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
        >
          {TEAMS.map((team) => (
            <option key={team} value={team}>{team}</option>
          ))}
        </select>
      </div>
    </div>
  )
}

function RuleRow({
  title,
  description,
  checked,
  onToggle,
  children,
}: {
  title: string
  description: string
  checked: boolean
  onToggle: (value: boolean) => void
  children?: ReactNode
}) {
  return (
    <div className="rounded-lg border border-ink-700 p-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium text-line-200">{title}</p>
          <p className="mt-0.5 text-xs text-muted-400">{description}</p>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={checked}
          onClick={() => onToggle(!checked)}
          className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${
            checked ? 'bg-teal-500' : 'bg-ink-700'
          }`}
        >
          <span
            className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition-transform ${
              checked ? 'translate-x-[18px]' : 'translate-x-1'
            }`}
          />
        </button>
      </div>
      {children}
    </div>
  )
}
