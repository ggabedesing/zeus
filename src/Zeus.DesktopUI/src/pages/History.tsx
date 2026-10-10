import { useEffect, useRef, useState } from 'react'
import { History as HistoryIcon, RefreshCw, Undo2 } from 'lucide-react'
import { native } from '@/lib/native'
import { withDeadline } from '@/components/ui/Async'
import { getLayoutById } from '@/layouts'

function canRevert(state: unknown) { return state === 'Applied' || state === 'Verified' || state === 'applied' || state === 'verified' || state === 'needs-review' }
function stateLabel(state: unknown) {
  const labels: Record<string, string> = { Applied: 'Aplicado', Verified: 'Aplicado e conferido', Reverted: 'Desfeito', Restored: 'Desfeito', RolledBack: 'Desfeito', Blocked: 'Alteração bloqueada', 'Needs-review': 'Requer conferência', Failed: 'Não concluído', Prepared: 'Preparado', Applying: 'Em aplicação', RevertFailed: 'Falha ao desfazer' }
  const normalized = typeof state === 'string' ? state.charAt(0).toUpperCase() + state.slice(1) : ''
  return labels[normalized] ?? 'Confira os detalhes'
}
export function History({ version, isTechnical, onChanged }: { version: number; isTechnical: boolean; onChanged: () => void }) {
  const [items, setItems] = useState<any[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const generation = useRef(0)
  async function refresh(clearError = true) {
    const request = ++generation.current
    setLoading(true); if (clearError) setError('')
    try { const history = await withDeadline(native.history()); if (request === generation.current) setItems(Array.isArray(history) ? history : []) }
    catch { if (request === generation.current) setError('Não foi possível abrir o Histórico. Tente novamente.') }
    finally { if (request === generation.current) setLoading(false) }
  }
  useEffect(() => { void refresh(); return () => { generation.current++ } }, [version])
  async function revert() {
    if (!pending || busy) return
    setBusy(true); setError(''); setNotice('')
    try { const result = await withDeadline(native.revert(pending), 65000); setNotice(result.message || 'Veja o resultado atualizado abaixo.'); setPending(null); onChanged(); await refresh() }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Não foi possível desfazer. O registro permanece no Histórico.'); await refresh(false) }
    finally { setBusy(false) }
  }
  return <div className="page enter"><div className="page-heading"><div><p className="eyebrow">VOCÊ NO CONTROLE</p><h1>Histórico de alterações</h1><p className="muted">Veja o que foi feito e restaure o papel de parede anterior.</p></div><button className="button secondary" disabled={loading || busy} onClick={() => void refresh()}><RefreshCw size={17} />{loading ? 'Carregando…' : 'Atualizar'}</button></div>
    {error && <div className="notice warning" role="alert">{error}</div>}{notice && <div className="notice" role="status">{notice}</div>}
    {!loading && !items.length && !error && <section className="panel empty-state"><HistoryIcon size={38} /><h2>Nenhuma alteração ainda</h2><p className="muted">Ao aplicar um papel de parede em Aparência, ele aparece aqui.</p></section>}
    {loading && !items.length && <p role="status" className="muted">Carregando suas alterações…</p>}
    <div className="history-list">{items.map(item => <section className="panel history-item" key={item.id}><div><span className="status-tag">{stateLabel(item.state)}</span><h2>{getLayoutById(item.layoutId)?.name ?? 'Papel de parede'}</h2><p className="small muted">{item.createdAt && !Number.isNaN(Date.parse(item.createdAt)) ? new Date(item.createdAt).toLocaleString('pt-BR') : 'Data não disponível'}</p><p>{item.message || 'Registro de alteração do papel de parede.'}</p>{isTechnical && <p className="small muted">Registro: {item.id} · Estado: {String(item.state)}</p>}</div>{canRevert(item.state) && <button className="button secondary" disabled={busy} onClick={() => setPending(item.id)}><Undo2 size={17} />{item.state === 'needs-review' ? 'Conferir e restaurar' : 'Desfazer'}</button>}</section>)}</div>
    {pending && <section className="panel confirmation" role="region" aria-labelledby="undo-title"><h2 id="undo-title">Restaurar o papel de parede anterior?</h2><p>O Zeus vai conferir se a alteração ainda pode ser desfeita e restaurar o fundo guardado neste registro.</p><div className="button-row"><button className="button primary" disabled={busy} onClick={() => void revert()}>{busy ? 'Restaurando…' : 'Sim, restaurar'}</button><button className="button secondary" disabled={busy} onClick={() => setPending(null)}>Cancelar</button></div></section>}
  </div>
}
