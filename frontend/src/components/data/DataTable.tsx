import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface Column<T> {
  key: string
  header: ReactNode
  cell: (row: T) => ReactNode
  className?: string
  headerClassName?: string
  align?: 'left' | 'right' | 'center'
  hideBelow?: 'sm' | 'md' | 'lg' | 'xl'
}

interface DataTableProps<T> {
  rows: T[]
  columns: Column<T>[]
  getRowId: (row: T) => string
  onRowClick?: (row: T) => void
  empty?: ReactNode
  className?: string
  rowClassName?: (row: T) => string | undefined
  caption?: string
}

const hideClasses = {
  sm: 'hidden sm:table-cell',
  md: 'hidden md:table-cell',
  lg: 'hidden lg:table-cell',
  xl: 'hidden xl:table-cell',
}

const alignClasses = { left: 'text-left', right: 'text-right', center: 'text-center' }

export function DataTable<T>({ rows, columns, getRowId, onRowClick, empty, className, rowClassName, caption }: DataTableProps<T>) {
  if (rows.length === 0 && empty) {
    return <div className={cn('p-4', className)}>{empty}</div>
  }

  return (
    <div className={cn('relative w-full overflow-x-auto', className)}>
      <table className="w-full border-collapse text-[12.5px]">
        {caption ? <caption className="sr-only">{caption}</caption> : null}
        <thead>
          <tr className="border-b border-line">
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={cn(
                  'whitespace-nowrap px-4 py-2.5 font-mono text-[10px] font-medium uppercase tracking-[0.12em] text-muted',
                  alignClasses[column.align ?? 'left'],
                  column.hideBelow ? hideClasses[column.hideBelow] : '',
                  column.headerClassName,
                )}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={getRowId(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              tabIndex={onRowClick ? 0 : undefined}
              onKeyDown={
                onRowClick
                  ? (event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault()
                        onRowClick(row)
                      }
                    }
                  : undefined
              }
              className={cn(
                'border-b border-line/70 last:border-0 transition-colors',
                onRowClick && 'cursor-pointer hover:bg-surface-2/70 focus-visible:bg-surface-2/70 focus-visible:outline-none',
                rowClassName?.(row),
              )}
            >
              {columns.map((column) => (
                <td
                  key={column.key}
                  className={cn(
                    'px-4 py-3 align-middle text-ink-2',
                    alignClasses[column.align ?? 'left'],
                    column.hideBelow ? hideClasses[column.hideBelow] : '',
                    column.className,
                  )}
                >
                  {column.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function TableSkeleton({ rows = 6, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <div className="divide-y divide-line" aria-busy="true" aria-label="Loading table">
      {Array.from({ length: rows }).map((_, rowIndex) => (
        <div key={rowIndex} className="flex items-center gap-4 px-4 py-3.5">
          {Array.from({ length: columns }).map((__, columnIndex) => (
            <div
              key={columnIndex}
              className="skeleton h-3 rounded"
              style={{ width: columnIndex === 0 ? '22%' : `${Math.max(10, 60 / columns)}%` }}
            />
          ))}
        </div>
      ))}
    </div>
  )
}
