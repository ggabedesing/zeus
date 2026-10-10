import { Cpu, MemoryStick, HardDrive, RefreshCw, ArrowRight, Palette, Monitor } from 'lucide-react'
interface Props { snapshot: any; performance: any; loading: boolean; error: string; onRefresh: () => void; onNavigate: (page: string) => void; isTechnical: boolean; computerOnly?: boolean }
const gb = (value: unknown) => typeof value === 'number' && Number.isFinite(value) ? `${(value / 1024 ** 3).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} GB` : 'Não disponível'
const percent = (value: unknown) => typeof value === 'number' && Number.isFinite(value) ? `${value.toFixed(0)}%` : 'Não disponível'
const text = (value: any) => typeof value === 'string' && value.trim() ? value : 'Não disponível'
export function Dashboard({ snapshot, performance, loading, error, onRefresh, onNavigate, isTechnical, computerOnly }: Props) {
  const total = performance?.totalMemoryBytes ?? snapshot?.memory?.totalBytes
  const available = performance?.availableMemoryBytes ?? snapshot?.memory?.availableBytes
  const used = typeof total === 'number' && typeof available === 'number' ? Math.max(0, total - available) : null
  const graphics = Array.isArray(snapshot?.graphics) ? snapshot.graphics : []
  const disks = Array.isArray(snapshot?.disks) ? snapshot.disks : []
  const time = performance?.collectedAt ?? snapshot?.collectedAt
  const warnings = [...(Array.isArray(snapshot?.warnings) ? snapshot.warnings : []), ...(Array.isArray(performance?.warnings) ? performance.warnings : [])]
  const metrics = [
    { title: 'Processador', value: percent(performance?.cpuPercent), detail: text(snapshot?.cpu?.name), icon: Cpu },
    { title: 'Memória em uso', value: gb(used), detail: `Total: ${gb(total)}`, icon: MemoryStick },
    { title: 'Placa de vídeo', value: graphics.length ? 'Identificada' : 'Não disponível', detail: graphics.map((item: any) => item.name).filter(Boolean).join(' · ') || 'Aguardando leitura do Windows', icon: Monitor },
    { title: 'Espaço livre', value: gb(disks[0]?.freeBytes), detail: disks[0] ? `${disks[0].driveLetter || disks[0].name || 'Disco'} · ${gb(disks[0].totalBytes)} no total` : 'Aguardando leitura do Windows', icon: HardDrive },
  ]
  return <div className="page enter">
    <div className="page-heading"><div><p className="eyebrow">{computerOnly ? 'SEU EQUIPAMENTO' : 'BEM-VINDO AO ZEUS'}</p><h1>{computerOnly ? 'Conheça seu PC' : 'Como está seu computador?'}</h1><p className="muted">{computerOnly ? 'Informações lidas do Windows, com clareza.' : 'Veja os dados do PC e escolha uma nova aparência.'}</p></div><button className="button secondary" onClick={onRefresh} disabled={loading}><RefreshCw size={17} className={loading ? 'spin' : ''} />{loading ? 'Lendo seu PC…' : 'Atualizar dados'}</button></div>
    {error && <div className="notice warning" role="alert">{error}<button onClick={onRefresh} disabled={loading}>Tentar novamente</button></div>}
    {!snapshot && !performance && loading && <div className="notice" role="status">Estamos lendo as informações do seu computador. Isso pode levar alguns segundos.</div>}
    <div className="metrics-grid">{metrics.map(({ title, value, detail, icon: Icon }) => <section className="panel metric" key={title}><div className="metric-top"><span>{title}</span><Icon size={20} /></div><strong>{value}</strong><p>{detail}</p></section>)}</div>
    <p className="reading-time">{time && !Number.isNaN(Date.parse(time)) ? `Última leitura: ${new Date(time).toLocaleString('pt-BR')}. Atualize para medir novamente.` : 'Nenhuma leitura concluída ainda.'}</p>
    {!computerOnly && <div className="home-actions"><section className="panel appearance-hero"><Palette size={28} /><h2>Deixe o PC com a sua cara</h2><p>Escolha um estilo, confira o papel de parede e aplique. O Zeus guarda o anterior para você desfazer.</p><button className="button primary" onClick={() => onNavigate('personalization')}>Escolher aparência <ArrowRight size={17} /></button></section><section className="panel"><Monitor className="accent" size={26} /><h2>Informação antes de mudar</h2><p className="muted">Esta leitura mostra uso e equipamento. Uma medição isolada não diz que há defeito ou que seu PC precisa de otimização.</p><button className="button secondary" onClick={() => onNavigate('computer')}>Ver meu PC <ArrowRight size={17} /></button><p className="small muted">Temperaturas e uso da GPU podem não estar disponíveis neste modo de leitura.</p></section></div>}
    {computerOnly && <div className="computer-grid"><section className="panel"><h2>Seu computador</h2><dl className="detail-list"><div><dt>Nome</dt><dd>{text(snapshot?.computerName)}</dd></div><div><dt>Windows</dt><dd>{typeof snapshot?.operatingSystem === 'string' ? snapshot.operatingSystem : text(snapshot?.operatingSystem?.caption ?? snapshot?.operatingSystem?.name)}</dd></div><div><dt>Processador</dt><dd>{text(snapshot?.cpu?.name)}</dd></div><div><dt>Núcleos</dt><dd>{snapshot?.cpu?.physicalCores ?? 'Não disponível'}</dd></div><div><dt>Memória total</dt><dd>{gb(total)}</dd></div></dl></section><section className="panel"><h2>Armazenamento</h2>{disks.length ? disks.map((disk: any, index: number) => <div className="disk-row" key={`${disk.driveLetter}-${index}`}><strong>{disk.driveLetter || disk.name || `Disco ${index + 1}`}</strong><p>{gb(disk.freeBytes)} livres de {gb(disk.totalBytes)}</p></div>) : <p className="muted">O Windows não retornou informações dos discos.</p>}</section></div>}
    {warnings.length > 0 && <div className="notice">Algumas informações não puderam ser obtidas. Isso não significa que seu computador está com defeito.{isTechnical && <ul>{warnings.map((warning: any, index: number) => <li key={index}>{typeof warning === 'string' ? warning : warning.message ?? 'Coletor sem informação'}</li>)}</ul>}</div>}
    {isTechnical && <details className="panel technical"><summary>Dados técnicos desta leitura</summary><pre>{JSON.stringify({ snapshot, performance }, null, 2)}</pre></details>}
  </div>
}
