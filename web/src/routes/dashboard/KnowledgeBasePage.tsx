import { useState } from 'react'
import clsx from 'clsx'
import { BookOpen, FileText, Globe2 } from 'lucide-react'
import { ManualEntriesTab } from '@/routes/dashboard/knowledge/ManualEntriesTab'
import { WebSourcesTab } from '@/routes/dashboard/knowledge/WebSourcesTab'

const TABS = [
  { key: 'manual', label: 'Manual Entries', icon: FileText },
  { key: 'web', label: 'Web Sources', icon: Globe2 },
] as const

type TabKey = (typeof TABS)[number]['key']

export function KnowledgeBasePage() {
  const [activeTab, setActiveTab] = useState<TabKey>('manual')

  return (
    <div>
      <header className="animate-reveal mb-6">
        <div className="mb-2 flex items-center gap-2">
          <BookOpen className="h-3.5 w-3.5 text-teal-400" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-teal-400">AI's source of truth</span>
        </div>
        <h1 className="text-2xl font-bold tracking-tight text-white">Knowledge Base</h1>
        <p className="mt-1 text-sm text-muted-400">
          Everything here is searchable by the AI when it answers customer questions.
        </p>
      </header>

      <div className="animate-reveal mb-6 inline-flex gap-1 rounded-xl border border-ink-700 bg-ink-900 p-1">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setActiveTab(tab.key)}
            className={clsx(
              'flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-medium transition-all duration-200',
              activeTab === tab.key
                ? 'bg-gradient-to-r from-teal-500/20 to-purple-500/15 text-mint-300 ring-1 ring-teal-500/25'
                : 'text-muted-400 hover:text-line-200',
            )}
          >
            <tab.icon className="h-3.5 w-3.5" />
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'manual' && <ManualEntriesTab />}
      {activeTab === 'web' && <WebSourcesTab />}
    </div>
  )
}
