import { Link } from 'react-router-dom'
import { Compass } from 'lucide-react'
import { NexusMark } from '@/components/brand/NexusMark'
import { Button } from '@/components/ui/button'
import { Panel } from '@/components/ui/card'

export function NotFoundPage() {
  return (
    <Panel className="mx-auto mt-10 max-w-lg p-8 text-center">
      <NexusMark className="mx-auto size-10" />
      <p className="mono-label mt-5">404 · route not found</p>
      <h1 className="mt-2 text-[22px] font-semibold text-ink">That workspace does not exist</h1>
      <p className="mt-1.5 text-[12.5px] leading-relaxed text-muted">
        The link may be outdated or the resource may have been removed. Return to the command centre to continue.
      </p>
      <Button asChild className="mt-5">
        <Link to="/">
          <Compass /> Back to command centre
        </Link>
      </Button>
    </Panel>
  )
}
