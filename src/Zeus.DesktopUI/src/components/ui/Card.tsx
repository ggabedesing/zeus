import { cn } from '@/lib/utils'
import { ReactNode } from 'react'

interface CardProps {
  children: ReactNode
  className?: string
  hover?: boolean
}

export function Card({ children, className, hover = false }: CardProps) {
  return (
    <div
      className={cn(
        'bg-surface-50 rounded-xl border border-surface-200/60 shadow-card',
        hover && 'transition-all duration-200 hover:border-zeus-500/30 hover:shadow-glow',
        className
      )}
    >
      {children}
    </div>
  )
}
