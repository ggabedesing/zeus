import { useEffect, useRef, useState, type CSSProperties, type PointerEvent } from 'react'
import { ArrowRight, CalendarDays, Check, ChevronDown, Clock3, Cpu, Folder, Grip, LayoutGrid, LoaderCircle, Maximize2, Monitor, Move, Palette, Play, Plus, RotateCcw, Search, Settings2, SlidersHorizontal, Square, StickyNote, X, Zap } from 'lucide-react'
import './environment.css'

type Widget = 'clock' | 'system' | 'notes' | 'dock' | 'topbar'
type Position = { x: number; y: number }
type Config = { theme: string; accent: string | null; motion: boolean; opacity: number; density: string; widgets: Record<Widget, boolean>; positions: Partial<Record<Widget, Position>>; pinned: string[]; notes: string; revision: number }
type Application = { id: string; name: string; source: string }
type Runtime = { running: boolean; state: string; layout?: string; error?: string; message?: string; hotkey_registered?: boolean | null }
type Snapshot = { supported: boolean; config: Config; applications: Application[]; runtime: Runtime }
type Wallpaper = { id: string; name: string }
type Theme = { name: string; background: string; surface: string; accent: string; text: string; muted: string; description: string }
const themes: Record<string, Theme> = {
  aurora: { name: 'Aurora', background: '#071b24', surface: '#142c37', accent: '#72e9c7', text: '#edfff9', muted: '#9cbcb9', description: 'Verde suave. Espaço para respirar.' },
  graphite: { name: 'Grafite', background: '#101319', surface: '#202630', accent: '#8da8c8', text: '#f0f4fa', muted: '#9caabb', description: 'Silêncio visual, tons de azul.' },
  light: { name: 'Luz', background: '#dce8f3', surface: '#f6f9fc', accent: '#5077bd', text: '#172b44', muted: '#60748c', description: 'Um horizonte claro para o dia.' },
  minimal: { name: 'Essencial', background: '#141619', surface: '#202327', accent: '#acb7ba', text: '#edf0f1', muted: '#929b9e', description: 'Só o que precisa estar aqui.' },
}
const initial: Config = { theme: 'aurora', accent: null, motion: true, opacity: .97, density: 'comfortable', widgets: { clock: true, system: true, notes: false, dock: true, topbar: true }, positions: {}, pinned: [], notes: '', revision: 0 }
const widgetInfo: { id: Widget; name: string; detail: string; icon: typeof Clock3 }[] = [
  { id: 'clock', name: 'Relógio & calendário', detail: 'Seu tempo, em primeiro plano.', icon: Clock3 },
  { id: 'system', name: 'Pulso do PC', detail: 'CPU e memória lidas do Windows.', icon: Cpu },
  { id: 'notes', name: 'Notas rápidas', detail: 'Escreva. Fica salvo neste PC.', icon: StickyNote },
  { id: 'dock', name: 'Dock de aplicativos', detail: 'Seus atalhos a um clique.', icon: LayoutGrid },
  { id: 'topbar', name: 'Barra do ambiente', detail: 'Data e acesso à busca.', icon: SlidersHorizontal },
]
const positions: Record<Widget, Position> = { clock: { x: .025, y: .12 }, system: { x: .975, y: .12 }, notes: { x: .975, y: .72 }, dock: { x: .5, y: .97 }, topbar: { x: .5, y: .025 } }
const builtin: Application[] = [{ id: 'builtin:files', name: 'Arquivos', source: 'Windows' }, { id: 'builtin:settings', name: 'Configurações', source: 'Windows' }, { id: 'builtin:zeus', name: 'Painel Zeus', source: 'Zeus' }]
const local = ['localhost', '127.0.0.1', '[::1]'].includes(location.hostname)

async function request<T>(body?: unknown): Promise<T> {
  const response = await fetch('/api/studio/environment', { ...(body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }), signal: AbortSignal.timeout(180000) })
  if (response.status === 404) throw new Error('Este motor ainda não contém o novo ambiente. Abra o executável do ZIP atualizado.')
  const data = await response.json()
  if (!response.ok) throw new Error(data.message || data.error || 'Não foi possível atualizar o ambiente.')
  return data
}

export default function Environment({ wallpapers = [], onApplied }: { wallpapers?: Wallpaper[]; onApplied?: () => void }) {
  const [config, setConfig] = useState<Config>(initial)
  const [apps, setApps] = useState<Application[]>(builtin)
  const [runtime, setRuntime] = useState<Runtime | null>(null)
  const [supported, setSupported] = useState(false)
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [search, setSearch] = useState('')
  const [background, setBackground] = useState(false)
  const [wallpaper, setWallpaper] = useState('')
  const [hideTaskbar, setHideTaskbar] = useState(false)
  const [review, setReview] = useState(false)
  const [showLauncher, setShowLauncher] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const dialog = useRef<HTMLDialogElement>(null)
  const previewDialog = useRef<HTMLDialogElement>(null)
  const preview = useRef<HTMLDivElement>(null)
  const drag = useRef<{ id: Widget; x: number; y: number; start: Position; width: number; height: number } | null>(null)
  const dirty = useRef(new Set<keyof Config>())
  const [now, setNow] = useState(new Date())
  const theme = themes[config.theme] || themes.aurora
  const color = config.accent || theme.accent
  const phase = runtime?.state?.toLowerCase()
  const active = runtime?.layout === 'environment' && ['starting', 'running', 'stopping'].includes(phase || '')
  const enabled = loaded && supported && !busy

  useEffect(() => {
    let cancelled = false
    if (local) request<Snapshot>().then(data => {
      if (cancelled) return
      setConfig(data.config); setApps(data.applications); setRuntime(data.runtime); setSupported(data.supported); setLoaded(true)
    }).catch(e => { if (!cancelled) setError(e.message) })
    return () => { cancelled = true }
  }, [])
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30000)
    return () => window.clearInterval(timer)
  }, [])
  useEffect(() => {
    if (!active) return
    let pending = false, cancelled = false
    const timer = window.setInterval(async () => {
      if (pending) return
      pending = true
      try {
        const response = await fetch('/api/studio/runtimes')
        if (!response.ok) throw new Error('Não foi possível ler o estado do ambiente.')
        const value = await response.json()
        if (!cancelled) setRuntime(value.layout_runtime)
      } catch (e) { if (!cancelled) setError(e instanceof Error ? e.message : 'Falha na leitura do ambiente.') }
      finally { pending = false }
    }, 2200)
    return () => { cancelled = true; window.clearInterval(timer) }
  }, [active])
  useEffect(() => { if (review) dialog.current?.showModal(); else dialog.current?.close() }, [review])
  useEffect(() => { if (expanded) previewDialog.current?.showModal(); else previewDialog.current?.close() }, [expanded])

  function change<K extends keyof Config>(key: K, value: Config[K]) {
    if (busy) return
    dirty.current.add(key)
    setConfig(current => ({ ...current, [key]: value })); setNotice('')
  }
  async function apply(command: 'save' | 'start' | 'stop') {
    setReview(false); setBusy(command); setError(''); setNotice('')
    try {
      // Preserve positions/notes changed by the native worker since this editor loaded.
      const settings = Object.fromEntries(Object.entries(config).filter(([key]) => dirty.current.has(key as keyof Config)))
      const value = await request<{ config: Config; runtime: Runtime; run?: { status: string }; message?: string }>({ command, ...(command !== 'stop' ? { config: settings } : {}), ...(command === 'start' ? { apply_background: background, wallpaper_id: wallpaper || null, hide_native_taskbar: hideTaskbar } : {}) })
      setRuntime(value.runtime)
      setConfig(value.config)
      dirty.current.clear()
      setNotice(value.message || (command === 'stop' ? 'Ambiente encerrado. Suas escolhas continuam salvas.' : command === 'save' ? 'Ambiente salvo. Se estiver ativo, os widgets recebem as mudanças automaticamente.' : 'Início solicitado. Acompanhe o estado do ambiente.'))
      if (value.run) onApplied?.()
    } catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível aplicar o ambiente.') }
    finally { setBusy('') }
  }
  function pin(id: string) {
    change('pinned', config.pinned.includes(id) ? config.pinned.filter(item => item !== id) : config.pinned.length < 8 ? [...config.pinned, id] : config.pinned)
  }
  function widgetStyle(id: Widget): CSSProperties {
    const position = config.positions[id] || positions[id]
    return { left: `${position.x * 100}%`, top: `${position.y * 100}%`, transform: `translate(${-position.x * 100}%, ${-position.y * 100}%)` }
  }
  function pointerDown(event: PointerEvent<HTMLDivElement>, id: Widget) {
    if ((event.target as HTMLElement).closest('button,input')) return
    const rect = event.currentTarget.parentElement!.getBoundingClientRect()
    const own = event.currentTarget.getBoundingClientRect()
    event.currentTarget.setPointerCapture(event.pointerId)
    drag.current = { id, x: event.clientX, y: event.clientY, start: config.positions[id] || positions[id], width: rect.width - own.width, height: rect.height - own.height }
  }
  function pointerMove(event: PointerEvent<HTMLDivElement>) {
    const value = drag.current
    if (!value) return
    change('positions', { ...config.positions, [value.id]: { x: Math.max(0, Math.min(1, value.start.x + (event.clientX - value.x) / Math.max(1, value.width))), y: Math.max(0, Math.min(1, value.start.y + (event.clientY - value.y) / Math.max(1, value.height))) } })
  }
  function widget(id: Widget, children: React.ReactNode) {
    return config.widgets[id] && <div className={`env-widget env-${id}`} style={widgetStyle(id)} tabIndex={0} role="group" aria-label={`${widgetInfo.find(item => item.id === id)?.name}. Use as setas para mover a prévia.`} onKeyDown={event => {
      if (event.target !== event.currentTarget || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return
      event.preventDefault()
      const value = config.positions[id] || positions[id], step = event.shiftKey ? .1 : .02
      change('positions', { ...config.positions, [id]: { x: Math.max(0, Math.min(1, value.x + (event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0))), y: Math.max(0, Math.min(1, value.y + (event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0))) } })
    }} onPointerDown={event => pointerDown(event, id)} onPointerMove={pointerMove} onPointerUp={() => { drag.current = null }} onPointerCancel={() => { drag.current = null }}>{children}</div>
  }
  const dockApps = [...builtin, ...config.pinned.map(id => apps.find(app => app.id === id)).filter((app): app is Application => !!app && !app.id.startsWith('builtin:'))]
  function appIcon(id: string) { return id === 'builtin:files' ? <Folder /> : id === 'builtin:settings' ? <Settings2 /> : id === 'builtin:zeus' ? <Zap /> : <span>{apps.find(app => app.id === id)?.name.slice(0, 1) || 'A'}</span> }
  function renderPreview(full = false) {
    const selectedWallpaper = background && wallpaper ? wallpapers.find(item => item.id === wallpaper) : undefined
    const style = { '--env-bg': theme.background, '--env-surface': theme.surface, '--env-accent': color, '--env-text': theme.text, '--env-muted': theme.muted, '--env-opacity': config.opacity, ...(selectedWallpaper ? { backgroundImage: `url('/api/studio/wallpapers/${encodeURIComponent(selectedWallpaper.id)}')`, backgroundSize: 'cover' } : {}) } as CSSProperties
    return <div ref={full ? undefined : preview} className={`env-desktop ${config.motion ? 'env-motion' : ''} ${config.density === 'compact' ? 'env-compact' : ''} ${full ? 'env-full' : ''}`} style={style}>
      {!selectedWallpaper && <div className="env-landscape" aria-hidden="true"><i /><i /><i /><i /><span>zeus<span>SEU ESPAÇO. SEU RITMO.</span></span></div>}
      {widget('topbar', <><span className="env-wordmark"><Zap />zeus</span><span className="env-space-label">SEU ESPAÇO</span><span className="env-bar-date">{now.toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span><button title="Abrir prévia da busca" onClick={() => setShowLauncher(!showLauncher)}><Search />Buscar aplicativos</button></>)}
      {widget('clock', <><div className="env-widget-eyebrow">SEU TEMPO<Grip /></div><strong className="env-big-time">{now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</strong><span className="env-weekday">{now.toLocaleDateString('pt-BR', { weekday: 'long' })}</span><div className="env-clock-bottom">{now.toLocaleDateString('pt-BR', { day: 'numeric', month: 'long' })}<CalendarDays /></div></>)}
      {widget('system', <><div className="env-widget-eyebrow">PULSO DO PC<Grip /></div><div className="env-preview-meter"><span>CPU<strong>24<small>%</small></strong></span><i><b style={{ width: '24%' }} /></i></div><div className="env-preview-meter"><span>RAM<strong>46<small>%</small></strong></span><i><b style={{ width: '46%' }} /></i></div><span className="env-micro">VALORES ILUSTRATIVOS</span></>)}
      {widget('notes', <><div className="env-widget-eyebrow">IDEIAS, SEM SAIR DAQUI<Grip /></div><p className="env-note-example">Um espaço para suas ideias.<br /><br />No desktop, clique e escreva.</p><span className="env-micro">SALVAS NESTE PC</span></>)}
      {widget('dock', <><button className="env-dock-search" title="Abrir prévia da busca" onClick={() => setShowLauncher(!showLauncher)}><Search /></button><span className="env-dock-divider" />{dockApps.slice(0, 7).map(app => <span className="env-app-tile" title={app.name} key={app.id}>{appIcon(app.id)}</span>)}<span className="env-dock-time">{now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span></>)}
      {showLauncher && <div className="env-preview-launcher"><div><span>O que vamos abrir?</span><button onClick={() => setShowLauncher(false)} aria-label="Fechar prévia da busca"><X /></button></div><span className="env-launcher-field"><Search />Busque seus aplicativos</span>{dockApps.slice(0, 3).map(app => <span className="env-launcher-result" key={app.id}>{appIcon(app.id)}{app.name}<ArrowRight /></span>)}<small>Prévia visual · no desktop, a busca abre seus aplicativos.</small></div>}
      <span className="env-preview-watermark">PRÉVIA DO AMBIENTE</span>
    </div>
  }

  return <section className="environment" aria-labelledby="env-title">
    <div className="env-intro"><div><div className="eyebrow small">UM AMBIENTE INTEIRO. COM A SUA CARA.</div><h2 id="env-title">Seu espaço.<br /><span>Outra sensação.</span></h2><p>Widgets, aplicativos e cores que conversam entre si.<br className="desktop-break" /> Monte o ambiente e leve para o seu Windows.</p></div><span className={`env-runtime-pill ${active ? 'active' : ''}`}><i />{active ? phase === 'starting' ? 'Iniciando ambiente' : phase === 'stopping' ? 'Encerrando' : 'Ambiente ativo' : 'Escolha. Combine. Ative.'}</span></div>
    <div className="env-workbench"><div className="env-preview-column"><div className="env-preview-toolbar"><span><Monitor size={14} />{theme.name}<span className="env-quiet-dot">·</span>Desktop Zeus</span><button onClick={() => setExpanded(true)}><Maximize2 size={13} />Ampliar prévia</button></div>{renderPreview()}<div className="env-preview-caption"><span><Move size={13} />Arraste os widgets ou use Tab e as setas para organizar.</span><button onClick={() => change('positions', {})}><RotateCcw size={12} />Reorganizar</button></div><div className="env-theme-picker" aria-label="Tema do ambiente">{Object.entries(themes).map(([id, value]) => <button key={id} className={config.theme === id ? 'selected' : ''} aria-pressed={config.theme === id} disabled={!!busy} onClick={() => { dirty.current.add('theme'); dirty.current.add('accent'); dirty.current.add('motion'); setConfig(current => ({ ...current, theme: id, accent: null, motion: id !== 'minimal' })); setNotice('') }}><span style={{ background: value.background, color: value.accent }}><i style={{ background: value.accent }} /><i style={{ background: value.surface }} />{config.theme === id && <Check size={13} />}</span><strong>{value.name}</strong></button>)}</div></div>
      <div className="env-controls"><div className="env-control-heading"><Palette size={15} /><strong>O seu toque</strong></div><label className="env-color-control"><span>Cor de destaque<small>{config.accent ? 'Sua cor' : 'Combina com o tema'}</small></span><input aria-label="Cor de destaque do ambiente" type="color" value={color} onChange={event => change('accent', event.target.value)} /></label><label className="env-select-control"><span>Densidade</span><span><select value={config.density} onChange={event => change('density', event.target.value)}><option value="comfortable">Confortável</option><option value="compact">Compacta</option></select><ChevronDown size={12} /></span></label><label className="env-range-control"><span>Opacidade<strong>{Math.round(config.opacity * 100)}%</strong></span><input type="range" min="80" max="100" value={Math.round(config.opacity * 100)} onChange={event => change('opacity', Number(event.target.value) / 100)} /></label><label className="env-motion-control"><span>Movimento suave</span><input type="checkbox" checked={config.motion} onChange={event => change('motion', event.target.checked)} /><i /></label><div className="env-control-divider" /><div className="env-control-heading"><LayoutGrid size={15} /><strong>O que fica à vista</strong></div>{widgetInfo.map(({ id, name, icon: Icon }) => <label className="env-widget-toggle" key={id}><Icon size={15} /><span>{name}</span><input type="checkbox" checked={config.widgets[id]} onChange={event => change('widgets', { ...config.widgets, [id]: event.target.checked })} /><i><Check size={11} /></i></label>)}<p className="env-control-note">No desktop, arraste pelo topo de cada widget. A busca fica no dock ou em Ctrl + Espaço, se o atalho estiver disponível.</p></div>
    </div>
    <div className="env-bottom-grid"><div className="env-pins"><div className="env-subheading"><div><span className="eyebrow small">MENOS CAMINHO ATÉ O QUE IMPORTA</span><h3>Seus aplicativos, por perto.</h3></div><span>{config.pinned.length}/8 fixados</span></div><label className="env-app-filter"><Search size={16} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar no Menu Iniciar" aria-label="Buscar aplicativos para fixar" /></label><div className="env-app-list">{apps.filter(app => !app.id.startsWith('builtin:') && app.name.toLocaleLowerCase().includes(search.toLocaleLowerCase())).slice(0, search ? 20 : 8).map(app => <button className={config.pinned.includes(app.id) ? 'pinned' : ''} key={app.id} aria-pressed={config.pinned.includes(app.id)} disabled={!config.pinned.includes(app.id) && config.pinned.length >= 8} onClick={() => pin(app.id)}><span>{app.name.slice(0, 1).toUpperCase()}</span><strong>{app.name}</strong>{config.pinned.includes(app.id) ? <Check size={14} /> : <Plus size={14} />}</button>)}{!apps.some(app => !app.id.startsWith('builtin:')) && <p className="env-empty-apps">Abra o motor atualizado para ver os aplicativos do seu Menu Iniciar. Arquivos, Configurações e Zeus já fazem parte do dock.</p>}</div>{config.pinned.length > 0 && <div className="env-pinned-chips">{config.pinned.map(id => <button key={id} onClick={() => pin(id)}>{apps.find(app => app.id === id)?.name || 'Aplicativo indisponível'}<X size={12} /></button>)}</div>}</div><div className="env-apply-card"><span className="env-apply-symbol"><Zap size={24} /></span><h3>Pronto para morar aqui?</h3><p>Ative os widgets e o launcher no seu desktop. Seus aplicativos continuam funcionando normalmente.</p><label className="env-background-option"><input type="checkbox" checked={background} onChange={event => setBackground(event.target.checked)} /><span>Aplicar também o tema e o fundo do Windows<small>Os ajustes reversíveis ficam no Histórico.</small></span></label>{background && <label className="env-background-select">Papel de parede<select value={wallpaper} onChange={event => setWallpaper(event.target.value)}><option value="">Fundo do tema {theme.name}</option>{wallpapers.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}<button className="primary" disabled={!enabled} onClick={() => setReview(true)}>{busy === 'start' ? <LoaderCircle className="spin" size={17} /> : <Play size={15} />}Ativar meu ambiente<ArrowRight size={16} /></button><div className="env-secondary-actions"><button disabled={!enabled} onClick={() => void apply('save')}>{busy === 'save' ? <LoaderCircle size={13} className="spin" /> : <Check size={13} />}Salvar escolhas</button><button disabled={!enabled || !active} onClick={() => void apply('stop')}><Square size={12} />Encerrar ambiente</button></div>{!enabled && !busy && <span className="env-local-copy">{local ? 'Requer o motor atualizado do Zeus no Windows.' : 'Disponível no aplicativo do Zeus para Windows.'}</span>}</div></div>
    {notice && <p className="env-notice" role="status"><Check size={16} />{notice}</p>}{(error || runtime?.error) && <div className="error-banner" role="alert"><span>{error || runtime?.error}</span><button onClick={() => setError('')} aria-label="Fechar mensagem"><X size={16} /></button></div>}
    <dialog className="modal env-review" ref={dialog} onCancel={() => setReview(false)} onClose={() => setReview(false)}><button className="modal-close" onClick={() => setReview(false)} aria-label="Fechar"><X size={19} /></button><span className="modal-icon"><Monitor size={25} /></span><div className="eyebrow small">SEU NOVO AMBIENTE</div><h2>Vamos levar para o desktop?</h2><p>O Zeus vai abrir {widgetInfo.filter(item => config.widgets[item.id]).map(item => item.name.toLocaleLowerCase()).join(', ') || 'o launcher pelo atalho disponível'} com o tema {theme.name}.</p><p>Os widgets ficam junto ao desktop. O launcher aparece quando você o abre. Fechar o Zeus encerra o ambiente; as escolhas continuam salvas.</p>{background && <p>O tema e o papel de parede do Windows também serão alterados. Você poderá restaurá-los no Histórico.</p>}<label className="env-background-option"><input type="checkbox" checked={hideTaskbar} onChange={event => setHideTaskbar(event.target.checked)} /><span>Ocultar temporariamente a barra do Windows<small>O Zeus guarda o estado para restaurar ao encerrar.</small></span></label><div className="modal-actions"><button className="secondary" onClick={() => setReview(false)}>Voltar</button><button className="primary" onClick={() => void apply('start')}><Play size={15} />Ativar ambiente</button></div></dialog>
    <dialog ref={previewDialog} className="env-preview-dialog" onCancel={() => setExpanded(false)} onClose={() => setExpanded(false)}><button className="env-preview-close" onClick={() => setExpanded(false)}><X size={18} />Fechar prévia</button>{renderPreview(true)}<p>Prévia visual do ambiente · as leituras de CPU e RAM acima são ilustrativas.</p></dialog>
  </section>
}
