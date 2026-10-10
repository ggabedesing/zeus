import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import { LucideIcon } from 'lucide-react'

interface MetricCardProps {
  title: string
  value: string | number
  unit?: string
  subtitle?: string
  icon: LucideIcon
  progress?: number // 0-100
  status?: 'good' | 'warning' | 'critical' | 'neutral'
  className?: string
}

const statusColors = {
  good: 'text-emerald-400',
  warning: 'text-amber-400',
  critical: 'text-rose-400',
  neutral: 'text-gray-400',
}

const progressColors = {
  good: 'bg-emerald-500',
  warning: 'bg-amber-500',
  critical: 'bg-rose-500',
  neutral: 'bg-zeus-500',
}

export function MetricCard({
  title,
  value,
  unit,
  subtitle,
  icon: Icon,
  progress,
  status = 'neutral',
  className,
}: MetricCardProps) {
  return (
    <Card className={cn('p-5', className)} hover>
      <div className="flex items-start justify-between mb-4">
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-surface-100">
            <Icon className="w-4 h-4 text-zeus-400" strokeWidth={1.75} />
          </div>
          <span className="text-sm font-medium text-gray-400">{title}</span>
        </div>
      </div>

      <div className="flex items-baseline gap-1.5 mb-1">
        <span className="text-3xl font-semibold tracking-tight text-white">
          {value}
        </span>
        {unit && (
          <span className="text-sm font-medium text-gray-500">{unit}</span>
        )}
      </div>

      {subtitle && (
        <p className={cn('text-sm', statusColors[status])}>{subtitle}</p>
      )}

      {typeof progress === 'number' && (
        <div className="mt-4 h-1.5 bg-surface-200 rounded-full overflow-hidden">
          <div
            className={cn(
              'h-full rounded-full transition-all duration-500',
              progressColors[status]
            )}
            style={{ width: `${Math.min(100, Math.max(0, progress))}%` }}
          />
        </div>
      )}
    </Card>
  )
}
