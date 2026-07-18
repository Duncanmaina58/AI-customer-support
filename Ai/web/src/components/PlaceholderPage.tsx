export function PlaceholderPage({ title, blurb }: { title: string; blurb: string }) {
  return (
    <div>
      <header className="mb-6">
        <h1 className="text-xl font-semibold text-white">{title}</h1>
        <p className="mt-1 text-sm text-muted-400">{blurb}</p>
      </header>
      <div className="rounded-xl border border-dashed border-ink-700 p-10 text-center">
        <p className="text-sm text-muted-400">This section is scheduled for a later sprint.</p>
      </div>
    </div>
  )
}
