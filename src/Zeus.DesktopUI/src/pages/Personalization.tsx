import { useEffect, useRef, useState } from 'react'
import { Check, ArrowRight, Undo2, Palette } from 'lucide-react'
import { layouts } from '@/layouts'
import { LayoutPreview } from '@/components/layouts/LayoutPreview'
import { native } from '@/lib/native'
import { withDeadline } from '@/components/ui/Async'

interface Props { isTechnical: boolean; onApplied: () => void; onHistory: () => void }
export function Personalization({ isTechnical, onApplied, onHistory }: Props) {
  const [selectedId, setSelectedId] = useState(layouts[0].id)
  const [plan, setPlan] = useState<any>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<any>(null)
  const [theme, setTheme] = useState(() => localStorage.getItem('zeus.accent') || '#3b5bff')
  const requestId = useRef(0)
  const selectionRef = useRef(selectedId)
  const confirmationRef = useRef<HTMLDivElement>(null)
  const selected = layouts.find(layout => layout.id === selectedId) ?? layouts[0]
  useEffect(() => { document.documentElement.style.setProperty('--accent', theme) }, [theme])
  useEffect(() => { if (plan) { confirmationRef.current?.focus(); confirmationRef.current?.scrollIntoView({ block: 'nearest' }) } }, [plan])
  function select(id: string) { selectionRef.current = id; requestId.current++; setSelectedId(id); setPlan(null); setError(''); setResult(null) }
  async function preview() {
    const generation = ++requestId.current
    const layoutId = selectedId
    setBusy(true); setError(''); setPlan(null); setResult(null)
    try { const next = await withDeadline(native.preview(layoutId)); if (generation === requestId.current && selectionRef.current === layoutId) setPlan(next) }
    catch (cause) { if (generation === requestId.current) setError(cause instanceof Error ? cause.message : 'Não foi possível preparar a alteração.') }
    finally { setBusy(false) }
  }
  async function apply() {
    if (!plan?.canApply || busy) return
    setBusy(true); setError('')
    try { const applied = await withDeadline(native.apply(plan.id), 65000); setResult(applied); setPlan(null); onApplied() }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'A alteração não foi concluída. Confira o Histórico antes de tentar novamente.'); onApplied() }
    finally { setBusy(false) }
  }
  return <div className="page enter">
    <div className="page-heading"><div><p className="eyebrow">DO SEU JEITO</p><h1>Uma nova aparência para seu PC</h1><p className="muted">Comece pelo papel de parede. Escolha, confira e aplique.</p></div></div>
    <div className="layout-picker" aria-label="Estilos de papel de parede">{layouts.map(layout => <button disabled={busy} key={layout.id} className={selectedId === layout.id ? 'layout-option chosen' : 'layout-option'} aria-pressed={selectedId === layout.id} onClick={() => select(layout.id)}><div className="layout-swatch" style={{ background: `radial-gradient(ellipse at 30% 20%, ${layout.colors.accent}88, transparent 70%), ${layout.colors.background}` }}>{selectedId === layout.id && <span className="selected-check"><Check size={16} /></span>}</div><strong>{layout.name}</strong><span>{layout.tagline}</span></button>)}</div>
    <div className="personalization-grid"><div><LayoutPreview layout={selected} imageUrl={plan?.wallpaperDataUrl ?? plan?.wallpaperUrl} /><p className="small muted preview-caption">A aplicação altera somente o papel de parede estático. Relógio, dock, widgets e animações no desktop não são instalados.</p></div><section className="panel plan-intro"><span className="eyebrow">ESTILO SELECIONADO</span><h2>{selected.name}</h2><p className="muted">Um fundo inspirado nas cores deste estilo, gerado localmente pelo Zeus.</p><ul className="benefits"><li><Check size={17} /> Papel de parede estático</li><li><Undo2 size={17} /> Anterior guardado para desfazer</li></ul><button className="button primary full" disabled={busy} onClick={() => void preview()}>{busy ? 'Preparando…' : 'Conferir alteração'}<ArrowRight size={17} /></button><p className="small muted">Você confirma na próxima etapa.</p></section></div>
    {error && <div className="notice warning" role="alert">{error}<button className="text-action" onClick={onHistory}>Ver Histórico</button></div>}
    {plan && <section className="panel confirmation" ref={confirmationRef} tabIndex={-1} aria-labelledby="confirm-title"><span className="eyebrow">ANTES DE APLICAR</span><h2 id="confirm-title">Confira o que vai mudar</h2><p>{plan.summary || 'O papel de parede do Windows será substituído pelo fundo escolhido.'}</p><ul>{(Array.isArray(plan.changes) ? plan.changes : []).map((change: string, index: number) => <li key={index}>{change}</li>)}</ul>{Array.isArray(plan.limitations) && plan.limitations.length > 0 && <div className="small muted"><p>Limites desta alteração:</p><ul>{plan.limitations.map((limit: string, index: number) => <li key={index}>{limit}</li>)}</ul></div>}<div className="button-row"><button className="button primary" onClick={() => void apply()} disabled={busy || !plan.canApply}>{busy ? 'Aplicando…' : 'Aplicar papel de parede'}</button><button className="button secondary" disabled={busy} onClick={() => setPlan(null)}>Cancelar</button></div>{!plan.canApply && <p className="small">Este PC não permite aplicar essa mudança neste momento.</p>}</section>}
    {result && <section className="notice" role="status"><strong>Resultado da alteração</strong><p>{result.message || 'Confira o resultado no Histórico.'}</p><button className="button secondary" onClick={onHistory}>Abrir Histórico e desfazer <Undo2 size={17} /></button>{isTechnical && <p className="small">Estado: {String(result.state)} · Registro: {String(result.id)}</p>}</section>}
    <section className="panel app-theme"><div><Palette size={20} /><h2>Cor do Zeus</h2><p className="muted">Muda só a cor dos botões deste aplicativo.</p></div><div className="color-options">{['#3b5bff', '#0065d3', '#5855cf', '#007c82', '#8840c6'].map(color => <button key={color} aria-label={`Usar cor ${color} no aplicativo`} aria-pressed={theme === color} className={theme === color ? 'color-option active' : 'color-option'} style={{ backgroundColor: color }} onClick={() => { localStorage.setItem('zeus.accent', color); setTheme(color) }}>{theme === color && <Check size={17} />}</button>)}</div></section>
    {isTechnical && <details className="panel technical"><summary>Referências técnicas do estilo</summary><p>Os componentes abaixo são referências do catálogo original. Não são executados pelo Zeus nesta versão.</p><ul>{selected.components.map(component => <li key={component.name}><strong>{component.name}</strong> — {component.description}{component.tools?.length ? ` Referências: ${component.tools.join(', ')}.` : ''}</li>)}</ul></details>}
  </div>
}
