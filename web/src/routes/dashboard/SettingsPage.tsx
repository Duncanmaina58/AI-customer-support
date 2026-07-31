import { useState } from 'react'
import clsx from 'clsx'
import { Settings2, Building2, Radio, CreditCard, Users, ShieldCheck } from 'lucide-react'
import { CompanySettingsTab } from '@/routes/dashboard/settings/CompanySettingsTab'
import { TeamSettingsTab } from '@/routes/dashboard/settings/TeamSettingsTab'
import { ChannelsSettingsTab } from '@/routes/dashboard/settings/ChannelsSettingsTab'
import { BillingSettingsTab } from '@/routes/dashboard/settings/BillingSettingsTab'
import { SecuritySettingsTab } from '@/routes/dashboard/settings/SecuritySettingsTab'

const TABS = [
  { key: 'company', label: 'Company', icon: Building2 },
  { key: 'channels', label: 'Channels', icon: Radio },
  { key: 'billing', label: 'Billing', icon: CreditCard },
  { key: 'team', label: 'Team', icon: Users },
  { key: 'security', label: 'Security', icon: ShieldCheck },
] as const

type TabKey = (typeof TABS)[number]['key']

export function SettingsPage() {
  const [activeTab, setActiveTab] = useState<TabKey>('company')

  return (
    <div>
      <header className="animate-reveal mb-6">
        <div className="mb-2 flex items-center gap-2">
          <Settings2 className="h-3.5 w-3.5 text-teal-400" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-teal-400">Workspace</span>
        </div>
        <h1 className="text-2xl font-bold tracking-tight text-white">Settings</h1>
        <p className="mt-1 text-sm text-muted-400">Company details, API access, and your team.</p>
      </header>

      <div className="animate-reveal mb-6 flex gap-1 overflow-x-auto border-b border-ink-700">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setActiveTab(tab.key)}
            className={clsx(
              'flex shrink-0 items-center gap-1.5 border-b-2 px-4 py-2.5 text-sm font-medium transition-colors',
              activeTab === tab.key
                ? 'border-teal-400 text-mint-300'
                : 'border-transparent text-muted-400 hover:text-line-200',
            )}
          >
            <tab.icon className="h-3.5 w-3.5" />
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'company' && <CompanySettingsTab />}
      {activeTab === 'channels' && <ChannelsSettingsTab />}
      {activeTab === 'billing' && <BillingSettingsTab />}
      {activeTab === 'team' && <TeamSettingsTab />}
      {activeTab === 'security' && <SecuritySettingsTab />}
    </div>
  )
}
