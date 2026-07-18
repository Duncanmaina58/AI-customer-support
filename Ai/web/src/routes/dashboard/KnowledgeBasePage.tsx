import { useState } from 'react'
import clsx from 'clsx'
import { ManualEntriesTab } from '@/routes/dashboard/knowledge/ManualEntriesTab'
import { WebSourcesTab } from '@/routes/dashboard/knowledge/WebSourcesTab'

const TABS = [
  { key: 'manual', label: 'Manual Entries' },
  { key: 'web', label: 'Web Sources' },
] as const

type TabKey = (typeof TABS)[number]['key']

export function KnowledgeBasePage() {
  const [activeTab, setActiveTab] = useState<TabKey>('manual')

  return (
    <div>
      <header className="mb-6">
        <h1 className="text-xl font-semibold text-white">Knowledge Base</h1>
        <p className="mt-1 text-sm text-muted-400">
          Everything here is searchable by the AI when it answers customer questions.
        </p>
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

      {activeTab === 'manual' && <ManualEntriesTab />}
      {activeTab === 'web' && <WebSourcesTab />}
    </div>
  )
}
