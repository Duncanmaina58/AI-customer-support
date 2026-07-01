import { useState } from 'react'
import clsx from 'clsx'
import { CompanySettingsTab } from '@/routes/dashboard/settings/CompanySettingsTab'
import { TeamSettingsTab } from '@/routes/dashboard/settings/TeamSettingsTab'

const TABS = [
  { key: 'company', label: 'Company' },
  { key: 'team', label: 'Team' },
] as const

type TabKey = (typeof TABS)[number]['key']

export function SettingsPage() {
  const [activeTab, setActiveTab] = useState<TabKey>('company')

  return (
    <div>
      <header className="mb-6">
        <h1 className="text-xl font-semibold text-white">Settings</h1>
        <p className="mt-1 text-sm text-muted-400">Company details, API access, and your team.</p>
      </header>

      <div className="mb-6 flex gap-1 border-b border-ink-700">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setActiveTab(tab.key)}
            className={clsx(
              'border-b-2 px-4 py-2 text-sm font-medium transition-colors',
              activeTab === tab.key
                ? 'border-teal-400 text-mint-300'
                : 'border-transparent text-muted-400 hover:text-line-200',
            )}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'company' ? <CompanySettingsTab /> : <TeamSettingsTab />}
    </div>
  )
}
