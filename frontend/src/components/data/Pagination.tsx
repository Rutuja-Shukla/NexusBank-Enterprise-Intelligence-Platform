import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { formatNumber } from '@/lib/utils'

export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
  label = 'records',
}: {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
  label?: string
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-4 py-2.5">
      <p className="font-mono text-[10.5px] uppercase tracking-[0.1em] text-muted">
        Page {page} of {Math.max(totalPages, 1)} · {formatNumber(totalCount)} {label}
      </p>
      <div className="flex items-center gap-1.5">
        <Button variant="ghost" size="icon-sm" aria-label="Previous page" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft />
        </Button>
        <Button variant="ghost" size="icon-sm" aria-label="Next page" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          <ChevronRight />
        </Button>
      </div>
    </div>
  )
}
