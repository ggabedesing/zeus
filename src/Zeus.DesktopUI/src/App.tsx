import { useEffect, useRef, useState } from 'react'
import { Sidebar } from '@/components/layout/Sidebar'
import { Header } from '@/components/layout/Header'
import { Dashboard } from '@/pages/Dashboard'
import { Personalization } from '@/pages/Personalization'
import { History } from '@/pages/History'
import { native } from '@/lib/native'
import { withDeadline } from '@/components/ui/Async'
export default function App() {
  const [activePage, setActivePage] = useState('dashboard')
  const [isTechnical, setIsTechnical] = useState(() => localStorage.getItem('zeus.technical') === 'true')
  const [snapshot, setSnapshot] = useState<any>(null)
  const [performance, setPerformance] = useState<any>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [historyVersion, setHistoryVersion] = useState(0)
  const started = useRef(false)
  async function refresh() {
    setLoading(true); setError('')
    const results = await Promise.allSettled([
      withDeadline(native.diagnostics(), 65000).then(setSnapshot),
      withDeadline(native.performance()).then(setPerformance),
    ])
    if (results.some(result => result.status === 'rejected')) setError('Não foi possível ler todos os dados do PC. Tente novamente. A hora da última leitura identifica os dados anteriores.')
    setLoading(false)
  }
  useEffect(() => { if (!started.current) { started.current = true; void refresh() } }, [])
  return <div className="app-shell">
    <a className="skip-link" href="#main-content">Ir para o conteúdo</a>
    <Sidebar active={activePage} onNavigate={setActivePage} isTechnical={isTechnical} onToggleMode={() => {
      setIsTechnical(value => { localStorage.setItem('zeus.technical', String(!value)); return !value })
    }} />
    <div className="app-body"><Header isTechnical={isTechnical} /><main id="main-content" tabIndex={-1}>
      {(activePage === 'dashboard' || activePage === 'computer') && <Dashboard snapshot={snapshot} performance={performance} loading={loading} error={error} onRefresh={() => void refresh()} onNavigate={setActivePage} isTechnical={isTechnical} computerOnly={activePage === 'computer'} />}
      {activePage === 'personalization' && <Personalization isTechnical={isTechnical} onApplied={() => setHistoryVersion(value => value + 1)} onHistory={() => setActivePage('history')} />}
      {activePage === 'history' && <History version={historyVersion} isTechnical={isTechnical} onChanged={() => setHistoryVersion(value => value + 1)} />}
    </main></div>
  </div>
}
