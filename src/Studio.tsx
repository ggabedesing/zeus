import { useEffect, useRef, useState } from 'react'
import { ArrowRight, Check, CheckCircle2, ChevronDown, CircleHelp, Cpu, Gamepad2, ImagePlus, LoaderCircle, Monitor, Palette, RefreshCw, ShieldCheck, SlidersHorizontal, Sparkles, Upload, X } from 'lucide-react'
import type { Run } from './types'
import './studio.css'

type Option = { value: string | number | boolean; label: string }
type Preference = { id: string; title: string; description: string; category: string; kind: 'toggle' | 'select'; options: Option[]; restart: boolean; requires_admin: boolean }
type Preset = { id: string; name: string; description: string; colors: { background: string; surface: string; accent: string; text: string }; theme: string; transparency: boolean; animations: boolean; wallpaper_id: string }
type Wallpaper = { id: string; name: string; format: string; width: number; height: number }
type RuntimeStatus = { running: boolean; active?: boolean; supported: boolean; state?: string; message?: string; error?: string }
type LayoutRuntime = RuntimeStatus & { layout?: string }
type LayoutOption = { id: 'dock' | 'sidebar'; name: string; description: string }
type DesktopScene = { id: string; name: string; description: string; colors: string[] }
type DesktopRuntime = RuntimeStatus & { scene: string | null; fps?: number }
type RuntimeResponse = { desktop_runtime?: DesktopRuntime; layout_runtime?: LayoutRuntime }
type ReviewTask = Maintenance & { layout?: { command: 'start' | 'stop'; layout?: 'dock' | 'sidebar'; hide_native_taskbar?: boolean }; desktop?: { command: 'start' | 'stop'; scene?: string } }
type Maintenance = { id: string; title: string; description: string; requires_admin: boolean }
type Job = { id: string; operation: string; title: string; state: 'queued' | 'running' | 'succeeded' | 'failed'; stage: string; output: string; message?: string; started_at: number; ended_at?: number }
type Catalog = { layout_runtime?: { layouts: LayoutOption[] }; desktop_runtime?: { scenes: DesktopScene[] }; preferences: Preference[]; personalization: { presets: Preset[]; wallpapers: Wallpaper[]; capabilities: Record<string, unknown> }; maintenance: Maintenance[] }
type StudioState = { lock_screen?: { supported: boolean; available: boolean; message?: string }; layout_runtime?: LayoutRuntime; desktop_runtime?: DesktopRuntime; supported: boolean; admin: boolean; preferences: Record<string, { available: boolean; value: unknown; error?: string }>; personalization: Record<string, unknown>; gpu: { adapters: { name: string; driver: string; vram_gb?: number; temperature_c?: number; utilization_pct?: number }[]; applications: { id: string; name: string; path: string; preference: string }[]; error?: string }; jobs: Job[] }
type Pending = { key?: string; id: string; value: unknown; title: string; detail: string }
const localHost = ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)
const previewPresets: Preset[] = [
  { id: 'preview-dark', name: 'Noite limpa', description: 'Windows escuro, contraste suave e foco no conteúdo.', colors: { background: '#131a20', surface: '#222e36', accent: '#a7c9eb', text: '#eef3fa' }, theme: 'dark', transparency: true, animations: true, wallpaper_id: '' },
  { id: 'preview-lime', name: 'Energia Zeus', description: 'Tons escuros com um toque de verde no Windows.', colors: { background: '#151c13', surface: '#283522', accent: '#cefa69', text: '#f3f8e9' }, theme: 'dark', transparency: true, animations: true, wallpaper_id: '' },
  { id: 'preview-light', name: 'Luz natural', description: 'Superfícies claras, detalhes leves e espaço para respirar.', colors: { background: '#ccd8d1', surface: '#f0f4f0', accent: '#397c6c', text: '#172c27' }, theme: 'light', transparency: true, animations: true, wallpaper_id: '' },
]
async function studioApi<T>(path: string, body?: unknown): Promise<T> {
  if (!localHost) throw new Error('Abra o Zeus.exe no seu Windows para usar esta função.')
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), path.endsWith('/apply') ? 180000 : 45000)
  try {
    const response = await fetch(`/api/studio${path}`, { signal: controller.signal, ...(body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }) })
    const value = await response.json().catch(() => null)
    if (!response.ok || !value) throw new Error(value?.message || value?.error || 'Não foi possível conversar com o motor local do Zeus.')
    return value as T
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') throw new Error('A resposta demorou mais que o esperado. Atualize o estado antes de tentar novamente.')
    throw error
  } finally { window.clearTimeout(timeout) }
}
function normalizeRuntime<T extends RuntimeStatus>(runtime?: T): T | undefined {
  return runtime ? { ...runtime, state: runtime.state?.toLowerCase(), message: runtime.error || runtime.message } : undefined
}
function normalizeState(state: StudioState): StudioState {
  return { ...state, desktop_runtime: normalizeRuntime(state.desktop_runtime), layout_runtime: normalizeRuntime(state.layout_runtime) }
}
const runtimeIsActive = (runtime?: RuntimeStatus) => !!runtime && (runtime.active === true || runtime.running || ['starting', 'running', 'paused', 'stopping'].includes(runtime.state || ''))
const runtimeInTransition = (runtime?: RuntimeStatus) => ['starting', 'stopping'].includes(runtime?.state || '')
function runtimeLabel(runtime: RuntimeStatus | undefined, running: string, stopped: string) {
  if (runtime?.state === 'starting') return 'Preparando…'
  if (runtime?.state === 'stopping') return 'Encerrando…'
  if (runtime?.state === 'paused') return 'Animação pausada pelo Zeus'
  if (runtime?.state === 'error') return 'O motor precisa de atenção'
  return runtime?.running ? running : stopped
}
const activeJob = (job: Job) => job.state === 'queued' || job.state === 'running'
const stateLabels = { queued: 'Na fila', running: 'Em andamento', succeeded: 'Concluído', failed: 'Precisa de atenção' }

function ThemePreview({ preset, wallpaper }: { preset: Preset; wallpaper?: string }) {
  return <div className="studio-desktop" style={{ backgroundColor: preset.colors.background, color: preset.colors.text, '--preview-accent': preset.colors.accent, '--preview-surface': preset.colors.surface } as React.CSSProperties} aria-hidden="true">
    {wallpaper ? <img className="studio-preview-wallpaper" src={`/api/studio/wallpapers/${encodeURIComponent(wallpaper)}`} alt="" /> : <div className="studio-preview-orb" />}
    <div className="studio-window"><div className="studio-window-toolbar"><span /><span /><span /></div><div className="studio-window-layout"><div className="studio-window-sidebar"><i /><i /><i /><i /></div><div className="studio-window-content"><div className="studio-window-title" /><div className="studio-window-tiles"><i /><i /><i /></div><div className="studio-window-line" /><div className="studio-window-line short" /></div></div></div>
    <div className="studio-taskbar"><span className="studio-win-icon"><i /><i /><i /><i /></span><span /><span /><span /><span /></div>
  </div>
}

export default function Studio({ section, onApplied }: { section: 'personalization' | 'system'; onApplied?: () => void }) {
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [state, setState] = useState<StudioState | null>(null)
  const [loading, setLoading] = useState(localHost)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')
  const [pending, setPending] = useState<Pending[]>([])
  const [review, setReview] = useState<ReviewTask | 'changes' | null>(null)
  const [hideNativeTaskbar, setHideNativeTaskbar] = useState(false)
  const [run, setRun] = useState<Run | null>(null)
  const [jobs, setJobs] = useState<Job[]>([])
  const [extraWallpapers, setExtraWallpapers] = useState<Wallpaper[]>([])
  const [uploadName, setUploadName] = useState('')
  const [applicationPath, setApplicationPath] = useState('')
  const dialog = useRef<HTMLDialogElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const mounted = useRef(true)
  const personal = section === 'personalization'
  const enabled = localHost && !!state?.supported && !busy && !loading

  async function refresh() {
    setLoading(true); setError('')
    try {
      const [nextCatalog, nextState] = await Promise.all([studioApi<Catalog>('/catalog'), studioApi<StudioState>('/state')])
      if (!mounted.current) return
      setCatalog(nextCatalog); setState(normalizeState(nextState)); setJobs(nextState.jobs || [])
    } catch (e) { if (mounted.current) setError(e instanceof Error ? e.message : 'Não foi possível ler as configurações do Windows.') }
    finally { if (mounted.current) setLoading(false) }
  }
  useEffect(() => { mounted.current = true; if (localHost) void refresh(); return () => { mounted.current = false } }, [])
  useEffect(() => { setPending([]); setRun(null); setReview(null) }, [section])
  useEffect(() => { setHideNativeTaskbar(false); if (review) dialog.current?.showModal(); else dialog.current?.close() }, [review])
  const hasActiveJobs = jobs.some(activeJob)
  useEffect(() => {
    if (!hasActiveJobs || !localHost) return
    let cancelled = false
    let polling = false
    const timer = window.setInterval(async () => {
      if (polling) return
      polling = true
      try { const response = await studioApi<{ jobs: Job[] }>('/jobs'); if (!cancelled) setJobs(response.jobs) }
      catch (e) { if (!cancelled) setError(e instanceof Error ? e.message : 'Não foi possível atualizar a tarefa.') }
      finally { polling = false }
    }, 2500)
    return () => { cancelled = true; window.clearInterval(timer) }
  }, [hasActiveJobs])

  const hasActiveRuntime = runtimeIsActive(state?.desktop_runtime) || runtimeIsActive(state?.layout_runtime)
  useEffect(() => {
    if (!hasActiveRuntime || !localHost) return
    let cancelled = false
    let polling = false
    const timer = window.setInterval(async () => {
      if (polling) return
      polling = true
      try {
        const response = await studioApi<RuntimeResponse>('/runtimes')
        if (!cancelled) setState(current => current ? { ...current, desktop_runtime: normalizeRuntime(response.desktop_runtime), layout_runtime: normalizeRuntime(response.layout_runtime) } : current)
      } catch (e) { if (!cancelled) setError(e instanceof Error ? e.message : 'Não foi possível atualizar o motor da área de trabalho.') }
      finally { polling = false }
    }, 2500)
    return () => { cancelled = true; window.clearInterval(timer) }
  }, [hasActiveRuntime])

  function stage(item: Pending) {
    setPending(items => [...items.filter(existing => (existing.key || existing.id) !== (item.key || item.id)), item])
    setRun(null)
  }
  function preferenceValue(preference: Preference) { return pending.find(item => item.id === preference.id)?.value ?? state?.preferences[preference.id]?.value }
  function stagePreference(preference: Preference, value: unknown) {
    if (value === state?.preferences[preference.id]?.value) { setPending(items => items.filter(item => item.id !== preference.id)); return }
    stage({ id: preference.id, value, title: preference.title, detail: preference.options?.find(option => option.value === value)?.label || (value ? 'Ativar' : 'Desativar') })
  }
  async function apply() {
    if (!enabled || review !== 'changes' || !pending.length) return
    setReview(null); setBusy('apply'); setError('')
    try {
      const response = await studioApi<{ run: Run; state: StudioState }>('/apply', { items: pending.map(({ id, value }) => ({ id, value })) })
      setRun(response.run); setState(normalizeState(response.state)); setJobs(response.state.jobs || []); setPending([]); onApplied?.()
    } catch (e) { await refresh(); setError(e instanceof Error ? e.message : 'Não foi possível aplicar. Confira o estado antes de repetir.') }
    finally { setBusy('') }
  }
  async function startJob() {
    if (!enabled || !review || review === 'changes') return
    const operation = review
    setReview(null); setBusy(operation.id); setError('')
    try {
      if (operation.desktop || operation.layout) {
        if (operation.desktop) await studioApi('/desktop', operation.desktop)
        else await studioApi('/layout', { ...operation.layout, hide_native_taskbar: hideNativeTaskbar })
        const response = await studioApi<RuntimeResponse>('/runtimes')
        setState(current => current ? { ...current, desktop_runtime: normalizeRuntime(response.desktop_runtime), layout_runtime: normalizeRuntime(response.layout_runtime) } : current)
      } else {
        const response = await studioApi<{ job: Job }>('/jobs', { id: operation.id })
        setJobs(items => [response.job, ...items.filter(item => item.id !== response.job.id)])
      }
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível iniciar esta tarefa.') }
    finally { setBusy('') }
  }
  async function addApplication() {
    if (!enabled || !applicationPath.trim()) return
    setBusy('application'); setError('')
    try {
      const value = await studioApi<{ state: StudioState }>('/applications', { path: applicationPath })
      setState(value.state); setApplicationPath('')
    } catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível adicionar o aplicativo.') }
    finally { setBusy('') }
  }
  async function upload(file?: File) {
    if (!file || !enabled) return
    if (!/^image\/(jpeg|png|bmp)$/.test(file.type)) { setError('Escolha uma imagem JPG, PNG ou BMP.'); return }
    if (file.size > 20 * 1024 * 1024) { setError('Escolha uma imagem de até 20 MB.'); return }
    setBusy('upload'); setError('')
    try {
      const data = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result).split(',')[1]); reader.onerror = () => reject(new Error('Não foi possível ler esta imagem.')); reader.readAsDataURL(file) })
      const response = await studioApi<{ wallpaper: Wallpaper }>('/wallpapers', { filename: file.name, data })
      setExtraWallpapers(items => [response.wallpaper, ...items.filter(item => item.id !== response.wallpaper.id)])
      setUploadName(file.name); stage({ id: 'wallpaper', value: response.wallpaper.id, title: 'Papel de parede', detail: response.wallpaper.name })
    } catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível importar esta imagem.') }
    finally { setBusy(''); if (fileInput.current) fileInput.current.value = '' }
  }
  const presets = catalog?.personalization.presets || previewPresets
  const wallpapers = [...extraWallpapers, ...(catalog?.personalization.wallpapers || []).filter(wallpaper => !extraWallpapers.some(extra => extra.id === wallpaper.id))]
  const visualPreferences = (catalog?.preferences || []).filter(preference => /visual|personal|appear|animation|transparen|theme|accent|wallpaper|apar[eê]ncia|anima[cç]|tema|interface|barra de tarefas|explorer/i.test(`${preference.category} ${preference.id}`))
  const systemPreferences = (catalog?.preferences || []).filter(preference => !visualPreferences.includes(preference))
  const preferences = personal ? visualPreferences : systemPreferences
  const categories = [...new Set(preferences.map(preference => preference.category))]
  const gpuValue = (id: string) => (pending.find(item => item.key === `gpu-${id}`)?.value as { preference: string } | undefined)?.preference
  const changeCount = pending.length

  return <section className="studio-page" aria-labelledby="studio-title">
    <div className="studio-heading"><div><div className="eyebrow"><span />{personal ? 'SEU WINDOWS. DO SEU JEITO.' : 'CONTROLE ONDE FAZ DIFERENÇA.'}</div><h1 id="studio-title">{personal ? <>Mais a sua <span>cara.</span></> : <>Menos atrito.<br /><span>Mais controle.</span></>}</h1><p>{personal ? 'Temas, papel de parede e movimento para transformar a área de trabalho do Windows.' : 'Ajustes do Windows, preferências de GPU e ferramentas de manutenção em um lugar só.'}</p></div><div className="studio-heading-icon" aria-hidden="true">{personal ? <Palette size={43} strokeWidth={1.25} /> : <SlidersHorizontal size={43} strokeWidth={1.25} />}</div></div>
    <div className="studio-context"><span><Monitor size={16} />{state?.supported ? 'Alterações no Windows deste computador' : localHost ? 'Motor local do Windows' : 'Disponível no aplicativo para Windows'}</span>{localHost && <button className="text-button" onClick={refresh} disabled={loading || !!busy}><RefreshCw size={14} className={loading ? 'spin' : ''} />Atualizar leitura</button>}</div>
    {!localHost && <div className="studio-local-notice"><ShieldCheck size={21} /><div><strong>Abra o Zeus.exe para aplicar no seu PC.</strong><p>Esta página apresenta os recursos. O aplicativo local lê e altera as configurações reais do Windows.</p></div></div>}
    {localHost && state && !state.supported && <div className="studio-local-notice"><Monitor size={21} /><div><strong>O motor do Zeus precisa do Windows.</strong><p>Abra o Zeus no computador que você quer configurar.</p></div></div>}
    {error && <div className="error-banner" role="alert"><span>{error}</span><button onClick={() => setError('')} aria-label="Fechar mensagem"><X size={17} /></button></div>}
    {loading && !state && <div className="studio-loading" role="status"><LoaderCircle className="spin" size={23} />Lendo as configurações do seu Windows…</div>}
    {personal ? <>
      <div className="studio-section-title"><div><div className="eyebrow small">UM NOVO CLIMA PARA O SEU PC</div><h2>Escolha um visual.</h2></div><span className="studio-caption">{catalog ? 'Selecione e revise antes de aplicar' : 'Prévias ilustrativas · veja os temas no EXE'}</span></div>
      <div className="studio-theme-grid">{presets.map(preset => {
        const selected = pending.find(item => item.id === 'visual_preset')?.value === preset.id
        return <button key={preset.id} className={`studio-theme ${selected ? 'selected' : ''}`} aria-pressed={selected} disabled={!enabled || !catalog} onClick={() => stage({ id: 'visual_preset', value: preset.id, title: 'Visual do Windows', detail: preset.name })}><ThemePreview preset={preset} wallpaper={catalog && preset.wallpaper_id ? preset.wallpaper_id : undefined} /><span className="studio-theme-copy"><span className="studio-theme-name">{preset.name}<span className="studio-theme-check">{selected ? <Check size={13} /> : <ArrowRight size={14} />}</span></span><span className="studio-theme-description">{preset.description}</span><span className="studio-swatches" aria-hidden="true">{Object.values(preset.colors).map((color, index) => <i key={index} style={{ backgroundColor: color }} />)}</span></span></button>
      })}</div>
      <div className="studio-section-title"><div><div className="eyebrow small">O FUNDO DA SUA ROTINA</div><h2>Papel de parede.</h2></div><span className="studio-caption">Sua imagem na área de trabalho</span></div>
      <div className="studio-wallpaper-grid">{wallpapers.map(wallpaper => <button className={`studio-wallpaper ${pending.find(item => item.id === 'wallpaper')?.value === wallpaper.id ? 'selected' : ''}`} key={wallpaper.id} disabled={!enabled} onClick={() => stage({ id: 'wallpaper', value: wallpaper.id, title: 'Papel de parede', detail: wallpaper.name })}><img src={`/api/studio/wallpapers/${encodeURIComponent(wallpaper.id)}`} loading="lazy" alt={wallpaper.name} /><span>{wallpaper.name}<small>{wallpaper.width} × {wallpaper.height}</small></span>{pending.find(item => item.id === 'wallpaper')?.value === wallpaper.id && <span className="studio-wallpaper-check"><Check size={15} /></span>}</button>)}<button className="studio-upload" disabled={!enabled} onClick={() => fileInput.current?.click()}>{busy === 'upload' ? <LoaderCircle className="spin" size={28} /> : <ImagePlus size={28} />}<strong>{busy === 'upload' ? 'Importando imagem…' : 'Usar minha imagem'}</strong><span>JPG, PNG ou BMP · até 20 MB</span><span className="studio-upload-link"><Upload size={13} />Escolher arquivo</span></button></div>
      <input ref={fileInput} className="studio-file-input" type="file" accept="image/jpeg,image/png,image/bmp" aria-label="Importar papel de parede" onChange={event => void upload(event.target.files?.[0])} />{uploadName && <p className="studio-caption" role="status">{uploadName} importada. Revise para usar como papel de parede.</p>}
    </> : <>
      <div className="studio-section-title"><div><div className="eyebrow small">A PLACA CERTA PARA CADA TAREFA</div><h2>Gráficos & desempenho.</h2></div><span className="studio-caption"><Cpu size={14} />Leitura do hardware</span></div>
      <div className="studio-gpu-panel">{state?.gpu.adapters?.length ? <div className="studio-adapters">{state.gpu.adapters.map((adapter, index) => <article className="studio-adapter" key={`${adapter.name}-${index}`}><span className="studio-gpu-icon"><Cpu size={23} /></span><div><strong>{adapter.name}</strong><p>Driver {adapter.driver || 'não informado'}{adapter.vram_gb != null ? ` · ${adapter.vram_gb} GB de memória de vídeo` : ''}</p><div className="studio-gpu-facts">{adapter.temperature_c != null && <span>{adapter.temperature_c} °C</span>}{adapter.utilization_pct != null && <span>{adapter.utilization_pct}% de uso</span>}{adapter.temperature_c == null && adapter.utilization_pct == null && <span>Telemetria de temperatura e uso indisponível</span>}</div></div><span className="tag">Detectada</span></article>)}</div> : <div className="studio-compact-empty"><Cpu size={25} /><div><strong>{state ? 'Nenhuma GPU disponível na leitura.' : 'Sua GPU aparece no aplicativo local.'}</strong><p>Nome, driver e memória são lidos do computador.</p></div></div>}
        {state?.gpu.error && <p className="studio-inline-note">{state.gpu.error}</p>}
        <div className="studio-gpu-apps"><div className="studio-subheading"><strong>Preferência por aplicativo</strong><span>Vale para a próxima abertura do aplicativo</span></div>{state?.gpu.applications?.length ? state.gpu.applications.map(app => <label className="studio-app-row" key={app.id}><span className="studio-app-icon"><Gamepad2 size={17} /></span><span className="studio-app-copy"><strong>{app.name}</strong><small title={app.path}>{app.path}</small></span><select aria-label={`GPU para ${app.name}`} disabled={!enabled} value={gpuValue(app.id) ?? app.preference} onChange={event => { if (event.target.value === app.preference) { setPending(items => items.filter(item => item.key !== `gpu-${app.id}`)); return } stage({ key: `gpu-${app.id}`, id: 'gpu_preference', value: { application_id: app.id, preference: event.target.value }, title: `GPU · ${app.name}`, detail: ({ high: 'Alto desempenho', saving: 'Economia de energia', system: 'Decisão do Windows' })[event.target.value] || event.target.value }) }}><option value="system">Decisão do Windows</option><option value="high">Alto desempenho</option><option value="saving">Economia de energia</option></select></label>) : <p className="studio-inline-note">{state ? 'Nenhum aplicativo elegível em execução. Abra seu jogo ou aplicativo e atualize a leitura.' : 'Abra o Zeus.exe para ver os aplicativos em execução e escolher a preferência de GPU.'}</p>}</div>
      </div>
    </>}
    {categories.map(category => <div key={category} className="studio-preference-group"><div className="studio-section-title"><div><div className="eyebrow small">{personal ? 'AJUSTE OS DETALHES' : 'CONFIGURAÇÕES DO SISTEMA'}</div><h2>{({ visual: 'Aparência & movimento', personalization: 'Aparência & movimento', windows: 'Windows', gaming: 'Jogos', performance: 'Desempenho', privacy: 'Privacidade' } as Record<string, string>)[category.toLowerCase()] || category}</h2></div></div><div className="studio-preferences">{preferences.filter(preference => preference.category === category).map(preference => {
      const reading = state?.preferences[preference.id]
      const disabled = !enabled || !reading?.available || (preference.requires_admin && !state?.admin)
      const value = preferenceValue(preference)
      return <div className="studio-preference" key={preference.id}><div className="studio-preference-copy"><strong>{preference.title}{pending.some(item => item.id === preference.id) && <span className="studio-pending-dot" aria-label="Alteração selecionada" />}</strong><p>{preference.description}</p><div className="studio-preference-notes">{preference.restart && <span>Reinício pode ser necessário</span>}{preference.requires_admin && <span>Administrador</span>}{reading?.available && value == null && <span>Sem valor explícito no Windows. Escolha para definir.</span>}{!reading?.available && <span>{reading?.error || 'Indisponível nesta leitura'}</span>}</div></div>{preference.kind === 'toggle' && value != null ? <button type="button" role="switch" aria-checked={!!value} aria-label={preference.title} className="studio-switch" disabled={disabled} onClick={() => stagePreference(preference, !value)}><span /></button> : <div className="studio-select-wrap"><select value={preference.options.findIndex(option => option.value === value)} disabled={disabled} aria-label={preference.title} onChange={event => stagePreference(preference, preference.options[Number(event.target.value)].value)}><option value={-1} disabled>{reading?.available ? 'Escolher configuração…' : 'Não disponível'}</option>{preference.options.map((option, index) => <option value={index} key={index}>{option.label}</option>)}</select><ChevronDown size={14} aria-hidden="true" /></div>}</div>
    })}</div></div>)}
    {!personal && <div className="studio-lock-screen"><div><h3>Adicionar um jogo ou aplicativo</h3><p>Se o jogo não aparece na lista ou está fechado, indique seu arquivo .exe. O Zeus só o adiciona ao inventário gráfico; o arquivo não é executado.</p></div><form onSubmit={event => { event.preventDefault(); void addApplication() }}><label><span>Caminho completo do arquivo</span><input className="studio-application-path" value={applicationPath} onChange={event => setApplicationPath(event.target.value)} placeholder="C:\Jogos\MeuJogo\jogo.exe" disabled={!enabled} /></label><button className="secondary" disabled={!enabled || !applicationPath.trim()} type="submit">Adicionar aplicativo<ArrowRight size={14} /></button></form></div>}
    {personal && catalog?.personalization.capabilities.lock_screen_wallpaper === true && <div className="studio-lock-screen"><div><h3>Imagem da tela de bloqueio</h3><p>Use uma imagem da biblioteca na tela que aparece antes de entrar no Windows.</p><small>A restauração recupera a imagem estática; ela não recompõe Spotlight ou apresentação de slides.</small></div><label><span>Escolher imagem</span><select disabled={!enabled || !state?.lock_screen?.available} value={String(pending.find(item => item.id === 'lock_screen_wallpaper')?.value || '')} onChange={event => { if (event.target.value) stage({ id: 'lock_screen_wallpaper', value: event.target.value, title: 'Imagem da tela de bloqueio', detail: 'Aplicar a imagem escolhida. A restauração é da imagem estática, sem recompor Spotlight ou slides.' }) }}><option value="">Selecionar…</option>{wallpapers.map(image => <option key={image.id} value={image.id}>{image.name}</option>)}</select>{!state?.lock_screen?.available && <small>{state?.lock_screen?.message || 'Leitura da imagem anterior indisponível neste Windows.'}</small>}</label></div>}
    {personal && !!catalog?.layout_runtime?.layouts.length && <><div className="studio-section-title"><div><div className="eyebrow small">SEU ESPAÇO. SEU FLUXO.</div><h2>Um novo jeito de navegar.</h2></div><span className="studio-caption">Layouts próprios do Zeus para a área de trabalho</span></div><div className="studio-layout-panel"><div className="studio-layout-grid">{catalog.layout_runtime.layouts.map(layout => <article className={`studio-layout-card ${state?.layout_runtime?.running && state.layout_runtime.layout === layout.id ? 'selected' : ''}`} key={layout.id}><div className={`studio-layout-art layout-${layout.id}`} aria-hidden="true"><div className="studio-layout-art-window"><span /><span /><span /></div><div className="studio-layout-art-launcher"><i /><i /><i /><i /><i /></div></div><div className="studio-layout-copy"><h3>{layout.name}{state?.layout_runtime?.running && state.layout_runtime.layout === layout.id && <span className="tag">Em uso</span>}</h3><p>{layout.description}</p><button className="secondary" disabled={!enabled || !state?.layout_runtime?.supported || runtimeInTransition(state.layout_runtime)} onClick={() => setReview({ id: `layout-${layout.id}`, title: `Usar ${layout.name}`, description: layout.description, requires_admin: false, layout: { command: 'start', layout: layout.id } })}>Usar este layout<ArrowRight size={14} /></button></div></article>)}</div><div className="studio-layout-status"><p><strong>{runtimeLabel(state?.layout_runtime, 'Layout do Zeus em execução', 'Seu layout está pronto para escolher.')}</strong><br />{state?.layout_runtime?.message || 'O Zeus abre seu próprio painel de navegação sobre a área de trabalho. Você escolhe se quer ocultar a barra do Windows.'}</p>{runtimeIsActive(state?.layout_runtime) && <button className="secondary" disabled={!enabled || state?.layout_runtime?.state === 'stopping'} onClick={() => setReview({ id: 'layout-stop', title: 'Encerrar layout do Zeus', description: 'O painel será encerrado. Se a barra do Windows foi ocultada pelo Zeus, ela será restaurada.', requires_admin: false, layout: { command: 'stop' } })}>Encerrar layout<X size={14} /></button>}</div></div></>}
    {personal && !!catalog?.desktop_runtime?.scenes.length && <><div className="studio-section-title"><div><div className="eyebrow small">MOVIMENTO FEITO PELO ZEUS</div><h2>Sua área de trabalho, viva.</h2></div><span className="studio-caption">Motor nativo do Zeus · sem aplicativos externos</span></div><div className="studio-live-panel"><div className="studio-live-status"><span className={state?.desktop_runtime?.running ? 'dot live' : 'dot'} /><div><strong>{runtimeLabel(state?.desktop_runtime, 'Papel de parede animado em execução', 'Seu próximo cenário está aqui.')}</strong><p>{state?.desktop_runtime?.message || 'Escolha uma cena para usar como fundo da área de trabalho do Windows.'}</p></div>{runtimeIsActive(state?.desktop_runtime) && <button className="secondary" disabled={!enabled || state?.desktop_runtime?.state === 'stopping'} onClick={() => setReview({ id: 'desktop-stop', title: 'Parar papel de parede animado', description: 'O Zeus encerrará seu renderizador da área de trabalho.', requires_admin: false, desktop: { command: 'stop' } })}>Parar animação<X size={14} /></button>}</div><div className="studio-scene-grid">{catalog.desktop_runtime.scenes.map(scene => <article className={`studio-scene ${state?.desktop_runtime?.running && state.desktop_runtime.scene === scene.id ? 'selected' : ''}`} key={scene.id}><div className={`studio-scene-art scene-${scene.id}`} style={{ '--scene-one': scene.colors[0] || '#91bb60', '--scene-two': scene.colors[1] || '#1e6257', '--scene-three': scene.colors[2] || '#111c20' } as React.CSSProperties} aria-hidden="true"><i /><i /><i /><span>PRÉVIA DA CENA</span></div><div className="studio-scene-copy"><h3>{scene.name}{state?.desktop_runtime?.running && state.desktop_runtime.scene === scene.id && <span className="tag">Em uso</span>}</h3><p>{scene.description}</p><button className="secondary" disabled={!enabled || !state?.desktop_runtime?.supported || runtimeInTransition(state.desktop_runtime)} onClick={() => setReview({ id: `desktop-${scene.id}`, title: `Usar cena ${scene.name}`, description: scene.description, requires_admin: false, desktop: { command: 'start', scene: scene.id } })}>Usar no Windows<ArrowRight size={14} /></button></div></article>)}</div><p className="studio-live-note">O motor permanece ativo para desenhar o fundo e consome recursos de CPU/GPU. Você pode parar a animação quando quiser.</p></div></>}
    {!personal && <><div className="studio-section-title"><div><div className="eyebrow small">FERRAMENTAS DO PRÓPRIO WINDOWS</div><h2>Manutenção, sem mistério.</h2></div><span className="studio-admin"><ShieldCheck size={14} />{state?.admin ? 'Executando como administrador' : 'O Windows pedirá administrador quando necessário'}</span></div><div className="studio-maintenance-grid">{catalog?.maintenance.length ? catalog.maintenance.map(operation => <article className="studio-maintenance" key={operation.id}><span className="studio-maintenance-icon"><ShieldCheck size={20} /></span><h3>{operation.title}</h3><p>{operation.description}</p><button className="secondary" disabled={!enabled || hasActiveJobs} onClick={() => setReview(operation)}>Revisar tarefa<ArrowRight size={14} /></button>{operation.requires_admin && !state?.admin && <small>O Windows pedirá permissão de administrador.</small>}</article>) : <div className="studio-compact-empty"><ShieldCheck size={27} /><div><strong>Diagnóstico e manutenção no aplicativo local.</strong><p>Verificação do sistema, reparo, otimização de armazenamento e diagnóstico de rede.</p></div></div>}</div>
      {jobs.length > 0 && <div className="studio-jobs"><div className="studio-section-title"><h2>Atividade de manutenção.</h2></div>{jobs.map(job => <article className={`studio-job ${job.state}`} key={job.id}><div className="studio-job-header">{activeJob(job) ? <LoaderCircle size={19} className="spin" /> : job.state === 'succeeded' ? <CheckCircle2 size={19} /> : <CircleHelp size={19} />}<div><h3>{job.title}</h3><p>{job.stage || stateLabels[job.state]}</p></div><span className={`tag ${job.state === 'failed' ? 'amber' : ''}`}>{stateLabels[job.state]}</span></div>{activeJob(job) && <div className="indeterminate"><span /></div>}{job.message && <p className="studio-job-message">{job.message}</p>}{job.output && <details><summary>Ver saída do Windows</summary><pre>{job.output}</pre></details>}</article>)}</div>}
    </>}
    {run && <div className="studio-run" role="status"><div className="studio-subheading"><strong>{run.results.every(result => result.ok) ? 'Alterações conferidas.' : 'Confira o resultado de cada ajuste.'}</strong><button className="text-button" onClick={() => setRun(null)}>Fechar<X size={14} /></button></div>{run.results.map((result, index) => <div className="studio-run-row" key={index}>{result.ok ? <CheckCircle2 size={17} /> : <CircleHelp size={17} />}<div><strong>{result.title}</strong><p>{result.message || (result.ok ? 'Confirmado pelo motor local.' : 'O Windows não confirmou esta alteração.')}</p></div><span className={`tag ${result.ok ? '' : 'amber'}`}>{result.ok ? 'Confirmado' : 'Atenção'}</span></div>)}</div>}
    {busy === 'apply' && <div className="studio-applying" role="status"><LoaderCircle size={20} className="spin" /><span>Aplicando e conferindo no Windows…</span></div>}
    {changeCount > 0 && <div className="studio-review-bar"><div><strong>{changeCount} {changeCount === 1 ? 'alteração selecionada' : 'alterações selecionadas'}</strong><span>Revise os detalhes antes de aplicar ao Windows.</span></div><button className="text-button" disabled={!!busy} onClick={() => setPending([])}>Descartar</button><button className="primary" disabled={!enabled} onClick={() => setReview('changes')}>Revisar alterações<ArrowRight size={16} /></button></div>}
    <div className="studio-bottom-note"><Sparkles size={15} /><p>{personal ? 'Temas alteram recursos nativos do Windows. A prévia ilustra a combinação de cores; o resultado varia com a versão do sistema.' : 'O Zeus mostra apenas leituras disponíveis. Preferência de GPU não garante ganho de FPS e depende do hardware e do aplicativo.'}</p></div>
    <dialog ref={dialog} className="modal studio-review-modal" onCancel={() => setReview(null)} onClick={event => { if (event.target === event.currentTarget) setReview(null) }} aria-labelledby="studio-review-title"><button className="modal-close" onClick={() => setReview(null)} aria-label="Fechar revisão"><X size={19} /></button><span className="modal-icon">{review === 'changes' ? <SlidersHorizontal size={25} /> : <ShieldCheck size={25} />}</span><div className="eyebrow small">NO WINDOWS DESTE COMPUTADOR</div><h2 id="studio-review-title">{review === 'changes' ? 'Confira suas escolhas.' : review?.title}</h2>{review === 'changes' ? <><p>O Zeus vai aplicar estas configurações e registrar o resultado no histórico.</p><ul>{pending.map(item => <li key={item.key || item.id}><Check size={15} /><span>{item.title}<small>{item.detail}</small></span></li>)}</ul></> : <><p>{review?.description}</p><p>{review?.layout ? review.layout.command === 'start' ? 'O Zeus abrirá seu próprio painel na área de trabalho. O painel permanece em execução enquanto o layout estiver ativo e pode ser encerrado aqui.' : 'O painel do Zeus será fechado. A barra de tarefas do Windows será restaurada se tiver sido ocultada pelo Zeus.' : review?.desktop ? review.desktop.command === 'start' ? 'O renderizador do Zeus ficará em execução no plano de fundo para animar a área de trabalho. Ele usa CPU e GPU continuamente; você pode encerrar a animação pelo Zeus.' : 'A animação será encerrada e deixará de consumir recursos de renderização.' : 'A tarefa usa ferramentas do Windows e pode levar alguns minutos. O progresso e a saída ficam visíveis nesta página.'}</p>{review?.layout?.command === 'start' && <label className="studio-layout-consent"><input type="checkbox" checked={hideNativeTaskbar} onChange={event => setHideNativeTaskbar(event.target.checked)} /><span>Ocultar a barra de tarefas do Windows enquanto este layout estiver ativo.<small>O Zeus restaura a barra ao encerrar o layout.</small></span></label>}{review?.requires_admin && <p>O Windows poderá pedir permissão de administrador para executar esta tarefa.</p>}</>}<div className="modal-actions"><button className="secondary" onClick={() => setReview(null)}>Voltar</button><button className="primary" disabled={!enabled} onClick={() => void (review === 'changes' ? apply() : startJob())}>{review === 'changes' ? 'Aplicar ao Windows' : review?.layout ? review.layout.command === 'stop' ? 'Encerrar layout' : 'Iniciar layout' : review?.desktop ? review.desktop.command === 'stop' ? 'Parar animação' : 'Iniciar animação' : 'Iniciar tarefa'}<ArrowRight size={16} /></button></div></dialog>
  </section>
}
