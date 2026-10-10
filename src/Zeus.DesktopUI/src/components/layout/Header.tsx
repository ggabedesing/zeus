import { useEffect, useState } from 'react'
import { Clock3 } from 'lucide-react'
export function Header({ isTechnical }: { isTechnical: boolean }) {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 1000); return () => clearInterval(timer) }, [])
  return <header className="app-header"><span>{isTechnical ? 'Modo Técnico ativado' : 'Tudo em um só lugar'}</span><div className="header-clock"><Clock3 size={16} /><time dateTime={now.toISOString()}>{now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</time><span className="header-date">{now.toLocaleDateString('pt-BR', { day: 'numeric', month: 'long' })}</span></div></header>
}
