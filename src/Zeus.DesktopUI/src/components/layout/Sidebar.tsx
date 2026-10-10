import { House, Monitor, Palette, History, Wrench, Zap } from 'lucide-react'
const pages = [
  { id: 'dashboard', label: 'Início', icon: House }, { id: 'computer', label: 'Meu PC', icon: Monitor },
  { id: 'personalization', label: 'Aparência', icon: Palette }, { id: 'history', label: 'Histórico', icon: History },
]
interface Props { active: string; onNavigate: (id: string) => void; isTechnical: boolean; onToggleMode: () => void }
export function Sidebar({ active, onNavigate, isTechnical, onToggleMode }: Props) {
  return <aside className="sidebar">
    <div className="brand"><span className="brand-icon"><Zap size={22} /></span><div>Zeus PC<small>Seu computador, do seu jeito</small></div></div>
    <nav aria-label="Navegação principal">{pages.map(({ id, label, icon: Icon }) => <button key={id} aria-label={label} title={label} className={active === id ? 'nav-item selected' : 'nav-item'} aria-current={active === id ? 'page' : undefined} onClick={() => onNavigate(id)}><Icon size={20} /><span>{label}</span></button>)}</nav>
    <div className="sidebar-bottom"><button className="nav-item" aria-label="Modo Técnico" title="Modo Técnico" aria-pressed={isTechnical} onClick={onToggleMode}><Wrench size={18} /><span>Modo Técnico</span><span className={isTechnical ? 'toggle on' : 'toggle'} /></button><p>Mais detalhes, quando precisar.</p></div>
  </aside>
}
