import { Component, computed, inject, signal, output, input, effect, OnInit, HostListener } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { DesktopBridge } from '../../bridge';
import { IconComponent } from '../../icon';
import { TenenciaEditorComponent } from './tenencia-editor';
import { purchaseCandidates, csvCell } from './tenencia-report';
import { buildXlsx, Cell, loadLogo, Sheet, toBase64 } from './xlsx';
type GroupSort = 'classification' | 'name' | 'minimum' | 'status';
type ArticleSort = 'code' | 'description' | 'stock';
interface Article {
  code: string;
  description: string;
  stock: number | null;
  effectiveStock?: number | null;
  minimumStock?: number | null;
  issue?: string;
  expiry?: string | null;
  lastPurchase?: string | null;
  lastSale?: string | null;
  laboratory?: string | null;
  lastSupplier?: string | null;
  pvp?: number | string | null;
  homogeneous?: string | null;
  barcode?: string | null;
  classification?: string | null;
  inactive?: boolean;
  inactiveOn?: string | null;
}
export interface TenenciaFields {
  expiry: boolean;
  movements: boolean;
  purchase?: boolean;
  purchaseColumn?: string;
  homogeneous: string[];
}
export type ExpiryState = 'ok' | 'soon' | 'expired' | 'none';
/** Número máximo de artículos visibles por grupo antes de pulsar «Ver todos». */
export const VISIBLE_ARTICLES = 10;
interface Row {
  id: string;
  name: string;
  minimum: number;
  stock: number | null;
  missing: number | null;
  status: string;
  articles: Article[];
  requiresReview?: boolean;
  classification?: string;
  selectionNote?: string;
  sourceRow?: number;
}
interface Result {
  code: string;
  message: string;
  province: string;
  provinceName?: string;
  listVersion?: string;
  source?: string;
  checkedAt?: string;
  rows?: Row[];
  fields?: TenenciaFields;
  policy?: { expiryReviewMonths: number; countExpiredStock: boolean };
  cached?: boolean;
  customized?: boolean;
}
interface CotejoState {
  listVersion: string;
  counts: Record<string, Record<string, number>>;
  observation: string;
  checkedAt?: string | null;
  expiryReviewMonths: number;
  countExpiredStock: boolean;
  userName?: string | null;
  id?: string | null;
  title?: string;
  observationHtml?: string;
  attachments?: CotejoAttachment[];
}
interface CotejoAttachment { id: string; name: string; mime: string; size: number; base64: string }
interface TenenciaPolicySettings {
  expiryReviewMonths: number;
  countExpiredStock: boolean;
}
@Component({
  selector: 'viteka-tenencia',
  standalone: true,
  imports: [FormsModule, IconComponent, TenenciaEditorComponent],
  templateUrl: './tenencia.html',
  styleUrl: './tenencia.css',
})
export class TenenciaComponent implements OnInit {
  private bridge = inject(DesktopBridge);
  private sanitizer = inject(DomSanitizer);
  readonly back = output<void>();
  readonly summary = output<{ total: number; covered: number; shortfall: number; checkedAt: string }>();
  /** Identificador del usuario para recordar el último grupo consultado. */
  readonly userKey = input('');
  /** Búsqueda inicial (desde la paleta Ctrl+K). */
  readonly initialSearch = input('');
  /** Solo el superadministrador (Viteka) puede editar el listado. */
  readonly canConfigure = input(false);
  readonly canConfigureTenencia = input(false);
  /** Mensajes breves para el aviso flotante de la aplicación. */
  readonly toast = output<string>();
  readonly VISIBLE = VISIBLE_ARTICLES;
  readonly editing = signal(false);
  readonly view = signal<'query' | 'cotejo' | 'expiry'>('query');
  readonly cotejo = signal<CotejoState | null>(null);
  readonly cotejoHistory = signal<CotejoState[] | null>(null);
  readonly historyOpen = signal(false);
  readonly historyDelete = signal<CotejoState | null>(null);
  readonly attachmentViewer = signal<{ title: string; attachments: CotejoAttachment[] } | null>(null);
  readonly attachmentPreview = signal<CotejoAttachment | null>(null);
  readonly historySearch = signal('');
  readonly historyFilter = signal<'all' | 'complete' | 'pending' | 'draft'>('all');
  readonly documentationOpen = signal(true);
  readonly cotejoBusy = signal(false);
  readonly cotejoDirty = signal(false);
  readonly cotejoDraftSaved = signal(false);
  readonly leaveCotejo = signal<'query' | 'expiry' | 'back' | null>(null);
  readonly scanCode = signal('');
  readonly scanError = signal('');
  readonly overagePrompt = signal<{ row: Row; article: Article } | null>(null);
  readonly cotejoActiveId = signal<string | null>(null);
  readonly cotejoCompleted = signal<string | null>(null);
  readonly policyBusy = signal(false);
  readonly policy = signal<TenenciaPolicySettings | null>(null);
  cotejoObservation = '';
  cotejoTitle = 'Sin título';
  cotejoObservationHtml = '';
  cotejoAttachments: CotejoAttachment[] = [];
  readonly exporting = signal(false);
  readonly expanded = signal<string[]>([]);
  readonly showExpiry = computed(() => !!this.result()?.fields?.expiry);
  readonly totalMissing = computed(() =>
    this.rows().reduce((n, r) => n + (r.status === 'shortfall' ? (r.missing ?? 0) : 0), 0),
  );
  readonly busy = signal(false);
  readonly queryProgress = signal<number | null>(null);
  readonly result = signal<Result | null>(null);
  readonly error = signal('');
  readonly search = signal('');
  readonly status = signal('all');
  readonly activeId = signal<string | null>(null);
  readonly activeArticleCode = signal<string | null>(null);
  readonly selected = signal<Row | null>(null);
  private previousFocus: HTMLElement | null = null;
  readonly groupSort = signal<GroupSort | null>(null);
  readonly groupDescending = signal(false);
  // Por defecto, los artículos se ordenan de más a menos existencias.
  readonly articleSort = signal<ArticleSort | null>('stock');
  readonly articleDescending = signal(true);
  private collator = new Intl.Collator('es', { numeric: true, sensitivity: 'base' });
  readonly hasStock = computed(() => this.result()?.code === 'ok' && !!this.result()?.checkedAt);
  readonly rows = computed(() => this.result()?.rows ?? []);
  readonly cotejoActiveRow = computed(
    () => this.rows().find((row) => row.id === this.cotejoActiveId()) ?? this.rows()[0] ?? null,
  );
  readonly covered = computed(() => this.rows().filter((r) => r.status === 'covered').length);
  readonly shortfalls = computed(() => this.rows().filter((r) => r.status === 'shortfall').length);
  readonly expiryReviewMonths = computed(() => this.result()?.policy?.expiryReviewMonths ?? 3);
  readonly filtered = computed(() => {
    const q = this.search().trim().toLocaleLowerCase('es');
    return this.rows()
      .filter(
        (r) =>
          (this.status() === 'all' || (this.status() === 'expiry' ? this.needsExpiryReview(r) : r.status === this.status())) &&
          (!q ||
            r.name.toLocaleLowerCase('es').includes(q) ||
            (r.classification ?? '').toLocaleLowerCase('es').includes(q) ||
            r.articles.some((a) => a.code.includes(q) || a.description.toLocaleLowerCase('es').includes(q))),
      )
      .sort((a, b) => {
        const key = this.groupSort();
        if (!key) return 0;
        const x = key === 'status' ? this.label(a.status) : (a[key] ?? ''),
          y = key === 'status' ? this.label(b.status) : (b[key] ?? '');
        return (
          (typeof x === 'number' && typeof y === 'number' ? x - y : this.collator.compare(String(x), String(y))) *
          (this.groupDescending() ? -1 : 1)
        );
      });
  });
  readonly activeRow = computed<Row | null>(
    () => this.filtered().find((r) => r.id === this.activeId()) ?? this.filtered()[0] ?? null,
  );
  readonly activeArticle = computed(
    () => this.activeRow()?.articles.find((a) => a.code === this.activeArticleCode()) ?? null,
  );
  readonly canQuery = computed(() => !!this.result()?.listVersion && !this.busy());
  readonly coveredPercent = computed(() => (this.rows().length ? (this.covered() * 100) / this.rows().length : 0));
  /** Fecha y hora de la última consulta (también cuando se muestra la consulta memorizada). */
  readonly checkedTime = computed(() => this.stamp(this.result()?.checkedAt));
  constructor() {
    effect(() => {
      const q = this.initialSearch();
      if (q) {
        this.search.set(q);
        this.filter();
      }
    });
  }
  private groupKey() {
    return 'viteka.tenencia.group.' + this.userKey();
  }
  private restoreGroup() {
    if (this.activeId() || !this.userKey()) return;
    try {
      const id = localStorage.getItem(this.groupKey());
      if (id && this.rows().some((r) => r.id === id)) this.activeId.set(id);
    } catch {
      /* sin almacenamiento */
    }
  }
  toggleStatus(value: string) {
    this.status.set(this.status() === value ? 'all' : value);
    this.filter();
  }
  shortfallRows() {
    return this.rows().filter((r) => r.status === 'shortfall');
  }
  stamp(at?: string) {
    if (!at) return '';
    const d = new Date(at);
    return (
      d.toLocaleDateString('es-ES', { day: '2-digit', month: '2-digit', year: 'numeric' }) +
      ' ' +
      d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' })
    );
  }
  /** Faltas ordenadas de más a menos unidades pendientes. */
  shortfallsByMissing() {
    return this.shortfallRows().sort(
      (a, b) => (b.missing ?? 0) - (a.missing ?? 0) || this.collator.compare(a.name, b.name),
    );
  }
  private fileDay() {
    return (this.result()?.checkedAt ?? new Date().toISOString()).slice(0, 10);
  }
  private async save(name: string, bytes: Uint8Array) {
    const r = await this.bridge.call<{ saved: boolean; file?: string }>('file.save', { name, base64: toBase64(bytes) });
    if (r.saved) this.toast.emit('Guardado: ' + (r.file || name));
  }
  private async run(task: () => Promise<void>) {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.closeMenus();
    try {
      await task();
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.exporting.set(false);
    }
  }
  closeMenus() {
    document.querySelectorAll<HTMLDetailsElement>('details.export-menu[open]').forEach((d) => (d.open = false));
  }
  private expiryCell(a: Article): Cell {
    const state = this.expiryState(a);
    if (!state || !this.showExpiry()) return '';
    return {
      v: this.expiryText(a),
      s: state === 'ok' ? 'ok' : state === 'soon' ? 'soon' : state === 'expired' ? 'expired' : 'none',
    };
  }
  readonly reportOpen = signal(false);
  readonly reportMode = signal<'complete' | 'shortfalls' | 'expiry' | 'cotejo' | 'cotejo-shortfalls' | 'cotejo-all'>('shortfalls');
  readonly reportKind = signal<'xlsx' | 'csv' | 'txt' | 'doc' | 'pdf' | 'print'>('xlsx');
  readonly reportCotejo = signal<CotejoState | null>(null);
  readonly declarationGroups = signal<
    {
      id: string;
      name: string;
      minimum: number;
      codes: string[];
      descriptions?: Record<string, string>;
      atcPrefix?: string;
      classificationCode?: string;
    }[]
  >([]);
  readonly declarationPicks = signal<Record<string, string[]>>({});
  readonly declarationGroupId = signal('');
  readonly declarationReview = signal(false);
  readonly reportRows = computed(() => {
    const mode = this.reportMode();
    if (mode === 'shortfalls') return this.shortfallsByMissing();
    if (mode === 'expiry') return this.rows().filter((row) => this.needsExpiryReview(row)).map((row) => ({ ...row, articles: row.articles.filter((article) => this.requiresExpiryAttention(article)) }));
    if (mode === 'cotejo' || mode === 'cotejo-shortfalls' || mode === 'cotejo-all') {
      const state = this.reportCotejo() ?? this.cotejo();
      if (!state) return [];
      return this.rows().map((row) => {
        const articles = row.articles.filter((article) => (state.counts?.[row.id]?.[article.code] ?? 0) > 0);
        const counted = articles.reduce((sum, article) => sum + (state.counts?.[row.id]?.[article.code] ?? 0), 0);
        return { ...row, stock: counted, missing: Math.max(0, row.minimum - counted), status: counted >= row.minimum ? 'covered' : 'shortfall', articles };
      }).filter((row) => mode === 'cotejo-all' || (mode === 'cotejo-shortfalls' ? row.missing > 0 : row.articles.length > 0));
    }
    return this.rows();
  });
  readonly declarationRow = computed(
    () => this.reportRows().find((r) => r.id === this.declarationGroupId()) ?? this.reportRows()[0],
  );
  reportArticles(row: Row) {
    if (this.reportMode() === 'shortfalls') return purchaseCandidates(row.articles);
    if ((this.reportMode() === 'cotejo-all' || this.reportMode() === 'cotejo-shortfalls') && row.missing) {
      const source = this.rows().find((item) => item.id === row.id) ?? row;
      const suggestions = purchaseCandidates(source.articles).filter((a) => !row.articles.some((item) => item.code === a.code));
      return this.reportMode() === 'cotejo-shortfalls' ? suggestions : [...row.articles, ...suggestions];
    }
    return row.articles;
  }
  reportTitle() {
    return this.reportMode() === 'shortfalls' ? 'Tenencia obligatoria · Solo faltas'
      : this.reportMode() === 'expiry' ? 'Tenencia obligatoria · Revisar caducidad'
      : ['cotejo','cotejo-shortfalls','cotejo-all'].includes(this.reportMode()) ? (this.reportCotejo()?.title || this.cotejoTitle || 'Cotejo de existencias mínimas')
      : 'Tenencia obligatoria · Listado completo';
  }
  reportCriteria() {
    return this.reportMode() === 'shortfalls' ? 'Grupos con unidades pendientes y candidatos de reposición.'
      : this.reportMode() === 'expiry' ? `Artículos con stock caducados o que caducan en ${this.expiryReviewMonths()} meses o menos.`
      : this.reportMode() === 'cotejo' ? 'Solo artículos y unidades cotejadas.'
      : this.reportMode() === 'cotejo-shortfalls' ? `${this.cotejoMissingUnits(this.reportCotejo() ?? this.cotejo())} unidades pendientes, con artículos sugeridos para pedir.`
      : this.reportMode() === 'cotejo-all' ? `Cotejados y ${this.cotejoMissingUnits(this.reportCotejo() ?? this.cotejo())} unidades pendientes, con artículos sugeridos para pedir.`
      : 'Listado completo de grupos y artículos de tenencia.';
  }
  chosenForDeclaration(row: Row, a: Article) {
    return (this.declarationPicks()[row.id] ?? []).includes(a.code);
  }
  toggleDeclaration(row: Row, a: Article, on: boolean) {
    this.declarationPicks.update((p) => ({
      ...p,
      [row.id]: on ? [...new Set([...(p[row.id] ?? []), a.code])] : (p[row.id] ?? []).filter((c) => c !== a.code),
    }));
  }
  async openReport(kind: 'xlsx' | 'csv' | 'txt' | 'doc' | 'pdf' | 'print') {
    this.closeMenus();
    this.reportKind.set(kind);
    if (this.view() === 'cotejo' && !this.reportCotejo()) this.reportCotejo.set(this.cotejo());
    this.reportMode.set(this.view() === 'cotejo' ? 'cotejo' : this.reportMode().startsWith('cotejo') ? 'shortfalls' : this.reportMode());
    this.error.set('');
    this.reportOpen.set(true);
  }
  private reportLines(): unknown[][] {
    if (['cotejo','cotejo-shortfalls','cotejo-all'].includes(this.reportMode())) {
      const state=this.reportCotejo()??this.cotejo();
      const lines:unknown[][]=[['Código grupo','Grupo','Mínimo','Cotejado','Faltan','Estado','CN','Descripción','Unidades cotejadas']];
      for(const row of this.reportRows()) {
        const articles=this.reportArticles(row);
        if (!articles.length) lines.push([row.classification??'',row.name,row.minimum,row.stock??0,row.missing??0,'Pendiente','','Sin sugerencias disponibles',0]);
        for(const article of articles) {
          const count=state?.counts?.[row.id]?.[article.code]??0;
          lines.push([row.classification??'',row.name,row.minimum,row.stock??0,row.missing??0,count>0?'Cotejado':'Sugerencia de pedido',article.code,article.description,count]);
        }
      }
      return lines;
    }
    const lines: unknown[][] = [
      [
        'Código grupo',
        'Grupo',
        'Mínimo',
        'Existencias grupo',
        'Faltan',
        'CN',
        'Medicamento',
        'Existencias CN',
        'Última compra',
      ],
    ];
    for (const r of this.reportRows()) {
      const articles = this.reportArticles(r),
        base = [r.classification ?? '', r.name, r.minimum, r.stock ?? 0, r.missing ?? 0];
      if (!articles.length) lines.push([...base, '', '', '', '']);
      for (const a of articles)
        lines.push([
          ...base,
          a.code,
          a.description,
          a.stock ?? 0,
          a.lastPurchase ?? 'Sin fecha de compra',
        ]);
    }
    return lines;
  }
  async generateReport() {
    if ((!this.hasStock() && !['cotejo','cotejo-shortfalls','cotejo-all'].includes(this.reportMode())) || this.exporting()) return;
    return this.run(async () => {
      this.reportOpen.set(false);
      const kind = this.reportKind(), modeName={shortfalls:'faltas',complete:'completo',expiry:'caducidades',cotejo:'cotejados','cotejo-shortfalls':'faltas-cotejo','cotejo-all':'cotejo-completo'}[this.reportMode()],
        name = 'tenencia-' + modeName + '-' + this.fileDay();
      if (kind === 'csv') {
        const text =
          '\ufeff' +
          [
            [this.reportTitle()],
            [this.reportCriteria()],
            ['Consulta', this.result()?.checkedAt, 'Listado', this.result()?.listVersion],
            ...this.reportLines(),
          ]
            .map((l) => l.map(csvCell).join(';'))
            .join('\r\n');
        await this.save(name + '.csv', new TextEncoder().encode(text));
      } else if (kind === 'txt') {
        const text=[this.reportTitle(),this.reportCriteria(),'',...this.reportLines().map((line)=>line.join('\t'))].join('\r\n');
        await this.save(name+'.txt',new TextEncoder().encode('\ufeff'+text));
      } else if (kind === 'doc') {
        const escape=(value:unknown)=>String(value??'').replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
        const lines=this.reportLines();
        const html=`<!doctype html><html><head><meta charset="utf-8"><style>body{font-family:Arial;color:#153b35}h1{color:#07555c}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccd6cf;padding:6px}th{background:#e8efe8}</style></head><body><h1>${escape(this.reportTitle())}</h1><p>${escape(this.reportCriteria())}</p><table><tr>${lines[0].map((v)=>`<th>${escape(v)}</th>`).join('')}</tr>${lines.slice(1).map((line)=>`<tr>${line.map((v)=>`<td>${escape(v)}</td>`).join('')}</tr>`).join('')}</table></body></html>`;
        await this.save(name+'.doc',new TextEncoder().encode('\ufeff'+html));
      } else if (kind === 'xlsx') {
        const logo = await loadLogo(48);
        const lines = this.reportLines();
        const rows: Cell[][] = [
          [],
          [],
          [],
          [{ v: this.reportTitle(), s: 'title' }],
          [{ v: this.reportCriteria(), s: 'wrap' }],
          [
            {
              v: 'Consulta ' + this.stamp(this.result()?.checkedAt) + ' · Listado ' + this.result()?.listVersion,
              s: 'muted',
            },
          ],
          [],
          lines[0].map((v) => ({ v: String(v), s: 'header' })),
          ...lines
            .slice(1)
            .map((r) => r.map((v) => ({ v: typeof v === 'number' ? v : String(v ?? ''), s: 'wrap' as const }))),
        ];
        await this.save(
          name + '.xlsx',
          buildXlsx(
            [
              {
                name: this.reportMode() === 'shortfalls' ? 'Faltas' : this.reportMode() === 'expiry' ? 'Caducidades' : this.reportMode() === 'cotejo' ? 'Cotejo' : 'Completo',
                widths: this.reportMode() === 'cotejo' ? [12, 44, 9, 12, 9, 12, 10, 50, 14] : [12, 44, 9, 13, 9, 10, 50, 12, 16],
                rows,
                merges: this.reportMode() === 'cotejo' ? ['A4:I4', 'A5:I5', 'A6:I6'] : ['A4:I4', 'A5:I5', 'A6:I6'],
                freeze: 8,
                image: logo ?? undefined,
              },
            ],
            this.reportTitle(),
          ),
        );
      } else {
        // Allow Angular to draw the selected report before WebView2 captures it.
        document.body.classList.add('printing-tenencia');
        await new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
        try {
          if (kind === 'pdf') {
            const r = await this.bridge.call<{ saved: boolean; file?: string }>('file.pdf', { name: name + '.pdf' });
            if (r.saved) this.toast.emit('PDF guardado.');
          } else await this.bridge.call('tenencia.print');
        } finally {
          document.body.classList.remove('printing-tenencia');
        }
      }
    });
  }
  exportCsv() {
    return this.openReport('csv');
  }
  exportExcel() {
    return this.openReport('xlsx');
  }
  exportPdf() {
    return this.openReport('pdf');
  }
  print() {
    return this.openReport('print');
  }
  /** Caducidad solo para artículos con existencias: umbral definido por la farmacia. */
  expiryState(a: Article): ExpiryState | null {
    if (!((a.stock ?? 0) > 0)) return null;
    return expiryState(a.expiry, this.queryDate(), this.expiryReviewMonths());
  }
  expiryText(a: Article) {
    if (!a.expiry) return 'Sin fecha';
    const [y, m] = a.expiry.split('-');
    return `${m}/${y}`;
  }
  expiryTitle(a: Article) {
    const state = this.expiryState(a);
    return state === 'ok'
      ? `Caducidad correcta: más de ${this.expiryReviewMonths()} meses`
      : state === 'soon'
        ? `Caduca en ${this.expiryReviewMonths()} meses o menos`
        : state === 'expired'
          ? 'Caducado'
          : 'Sin fecha de caducidad en la ficha';
  }
  movementDate(s?: string | null) {
    if (!s) return '—';
    const [y, m, d] = s.split('-');
    return `${d}/${m}/${y}`;
  }
  detailDate(s?: string | null) {
    return s ? this.movementDate(s) : '';
  }
  private queryDate() {
    return this.result()?.checkedAt ? new Date(this.result()!.checkedAt!) : new Date();
  }
  needsExpiryReview(row: Row) {
    return row.articles.some((a) => this.requiresExpiryAttention(a));
  }
  requiresExpiryAttention(article: Article) {
    const state = this.expiryState(article);
    return state === 'soon' || state === 'expired';
  }
  groupStateLabel(row: Row) {
    if (row.status === 'covered') return 'Correcto';
    if (row.status === 'not_checked') return 'Sin consultar';
    const missing = row.missing ?? row.minimum;
    return missing >= row.minimum
      ? `Faltan todas las unidades: ${this.number(missing)}`
      : `Faltan ${this.number(missing)} unidades`;
  }
  pvp(value?: number | string | null) {
    if (value === null || value === undefined || value === '') return '';
    const amount = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(amount) ? new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR' }).format(amount) : String(value);
  }
  isExpanded(row: Row) {
    return this.expanded().includes(row.id);
  }
  toggleExpanded(row: Row) {
    this.expanded.update((list) => (list.includes(row.id) ? list.filter((x) => x !== row.id) : [...list, row.id]));
  }
  /**
   * Con más de 10 artículos se muestran los de existencias y, hasta completar 10, los de compra o venta
   * más reciente. Nunca se oculta un artículo con existencias.
   */
  visibleArticles(row: Row) {
    const sorted = this.sortedArticles(row);
    if (sorted.length <= VISIBLE_ARTICLES || this.isExpanded(row)) return sorted;
    return pickVisible(sorted, !!this.result()?.fields?.movements);
  }
  nextShortfall() {
    const list = this.filtered();
    if (!list.length) return;
    const start = list.findIndex((r) => r.id === this.activeRow()?.id);
    for (let i = 1; i <= list.length; i++) {
      const row = list[(start + i) % list.length];
      if (row.status === 'shortfall') {
        this.choose(row);
        this.focusGroup(row);
        return;
      }
    }
  }
  private stepGroup(step: number) {
    const list = this.filtered();
    const index = list.findIndex((r) => r.id === this.activeRow()?.id);
    const row = list[Math.max(0, Math.min(list.length - 1, index + step))];
    if (row) {
      this.choose(row);
      this.focusGroup(row);
    }
  }
  private focusGroup(row: Row) {
    setTimeout(() => {
      const button = document.querySelector<HTMLButtonElement>(
        `.group-button[aria-label="${CSS.escape('Seleccionar grupo ' + row.name)}"]`,
      );
      button?.focus();
      button?.scrollIntoView({ block: 'nearest' });
    }, 0);
  }
  @HostListener('document:keydown', ['$event']) shortcuts(e: KeyboardEvent) {
    if (e.ctrlKey || e.altKey || e.metaKey || this.selected() || document.querySelector('[role=dialog]')) return;
    const target = e.target as HTMLElement;
    const typing = ['INPUT', 'SELECT', 'TEXTAREA'].includes(target.tagName);
    if (typing || !this.rows().length) return;
    if (e.key === '/') {
      e.preventDefault();
      document.querySelector<HTMLInputElement>('.tenencia-filters input')?.focus();
    } else if (e.key.toLowerCase() === 'f' && this.hasStock()) {
      e.preventDefault();
      this.nextShortfall();
    } else if (
      (e.key === 'ArrowDown' || e.key === 'ArrowUp') &&
      !target.closest('.group-list') &&
      !target.closest('.tenencia-table')
    ) {
      e.preventDefault();
      this.stepGroup(e.key === 'ArrowDown' ? 1 : -1);
    }
  }
  sortGroups(key: GroupSort) {
    const current = this.activeRow()?.id;
    if (current) this.activeId.set(current);
    this.groupDescending.set(this.groupSort() === key ? !this.groupDescending() : false);
    this.groupSort.set(key);
  }
  sortArticles(key: ArticleSort) {
    this.articleDescending.set(this.articleSort() === key ? !this.articleDescending() : false);
    this.articleSort.set(key);
  }
  groupOrder(key: GroupSort) {
    return this.groupSort() !== key ? 'none' : this.groupDescending() ? 'descending' : 'ascending';
  }
  articleOrder(key: ArticleSort) {
    return this.articleSort() !== key ? 'none' : this.articleDescending() ? 'descending' : 'ascending';
  }
  sortedArticles(row: Row) {
    const key = this.articleSort();
    if (!key) return row.articles;
    return [...row.articles].sort((a, b) => {
      const aUnworked = a.issue === 'stock_not_found';
      const bUnworked = b.issue === 'stock_not_found';
      if (aUnworked !== bUnworked) return aUnworked ? 1 : -1;
      if (aUnworked) return this.collator.compare(a.description, b.description);
      const x = a[key],
        y = b[key];
      if (x === null || y === null) return x === y ? 0 : x === null ? 1 : -1;
      const order =
        (typeof x === 'number' && typeof y === 'number' ? x - y : this.collator.compare(String(x), String(y))) *
        (this.articleDescending() ? -1 : 1);
      // A igualdad de existencias, orden alfabético.
      return order || this.collator.compare(a.description, b.description);
    });
  }
  async ngOnInit() {
    await this.state();
  }
  async state() {
    this.busy.set(true);
    this.error.set('');
    this.close();
    try {
      const r = await this.bridge.call<Result>('tenencia.state');
      if (r.province !== this.result()?.province) {
        this.activeId.set(null);
        this.search.set(this.initialSearch());
      }
      this.status.set('all');
      this.result.set(r);
      this.expanded.set([]);
      this.restoreGroup();
      this.emitSummary(r);
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.busy.set(false);
    }
  }
  async query() {
    if (!this.canQuery()) return;
    this.busy.set(true);
    this.queryProgress.set(0);
    this.error.set('');
    this.selected.set(null);
    const started = Date.now();
    const timer = window.setInterval(() => {
      const elapsed = Date.now() - started;
      this.queryProgress.set(Math.min(92, Math.round(8 + 84 * (1 - Math.exp(-elapsed / 1800)))));
    }, 120);
    try {
      const [r] = await Promise.all([
        this.bridge.call<Result>('tenencia.query'),
        new Promise<void>((resolve) => window.setTimeout(resolve, 650)),
      ]);
      window.clearInterval(timer);
      if (r.code === 'ok') {
        this.queryProgress.set(100);
        this.result.set(r);
        this.emitSummary(r);
        this.toast.emit('Consulta completada · ' + this.stamp(r.checkedAt));
      } else {
        this.error.set(r.message || 'No se pudo completar la consulta de existencias.');
        this.status.set('all');
      }
    } catch (e) {
      window.clearInterval(timer);
      this.status.set('all');
      this.error.set(this.message(e));
    } finally {
      await new Promise<void>((resolve) => window.setTimeout(resolve, this.queryProgress() === 100 ? 320 : 80));
      this.queryProgress.set(null);
      this.busy.set(false);
    }
  }
  private emitSummary(r: Result) {
    if (r.code === 'ok' && r.checkedAt)
      this.summary.emit({
        total: this.rows().length,
        covered: this.covered(),
        shortfall: this.shortfalls(),
        checkedAt: r.checkedAt,
      });
  }
  async finishEditing(changed: boolean) {
    this.editing.set(false);
    if (changed) {
      this.activeId.set(null);
      await this.state();
    }
  }
  filter() {
    this.activeId.set(null);
    this.activeArticleCode.set(null);
  }
  choose(row: Row) {
    this.activeId.set(row.id);
    try {
      if (this.userKey()) localStorage.setItem(this.groupKey(), row.id);
    } catch {
      /* sin almacenamiento */
    }
    this.activeArticleCode.set(null);
  }
  chooseArticle(article: Article) {
    this.activeArticleCode.set(article.code);
  }
  async setView(view: 'query' | 'cotejo' | 'expiry') {
    if (this.view() === 'cotejo' && view !== 'cotejo' && (await this.prepareCotejoExit(view))) return;
    this.view.set(view);
    if (view === 'cotejo' && !this.cotejo()) await this.loadCotejo();
    if (view === 'expiry' && !this.policy()) await this.loadPolicy();
  }
  async requestBack() {
    if (this.view() === 'cotejo' && (await this.prepareCotejoExit('back'))) return;
    this.back.emit();
  }
  private hasCotejoWork() {
    return !!this.cotejoTitle.trim() && this.cotejoTitle.trim() !== 'Sin título' || !!this.cotejoObservationHtml.trim() || this.cotejoAttachments.length > 0 ||
      !!this.cotejo() && this.cotejoUnits(this.cotejo()!) > 0;
  }
  private async prepareCotejoExit(target: 'query' | 'expiry' | 'back') {
    if (!this.hasCotejoWork()) return false;
    if (this.cotejoDraftSaved() && !this.cotejoDirty()) {
      await this.clearCotejo();
      return false;
    }
    if (this.cotejo()?.checkedAt && !this.cotejoDirty()) {
      await this.clearCotejo();
      return false;
    }
    this.leaveCotejo.set(target);
    return true;
  }
  private finishCotejoExit() {
    const target = this.leaveCotejo();
    this.leaveCotejo.set(null);
    if (target === 'back') this.back.emit();
    else if (target) this.view.set(target);
  }
  chooseCotejoGroup(row: Row) {
    this.cotejoActiveId.set(row.id);
  }
  async loadCotejo() {
    this.cotejoBusy.set(true);
    try {
      const state = await this.bridge.call<CotejoState>('cotejo.state');
      this.cotejo.set({ ...state, counts: state.counts ?? {} });
      this.cotejoObservation = state.observation ?? '';
      this.cotejoTitle = state.title?.trim() || 'Sin título';
      this.cotejoObservationHtml = state.observationHtml || this.escapeHtml(state.observation ?? '');
      this.cotejoAttachments = [...(state.attachments ?? [])];
      this.cotejoDirty.set(false);
      this.cotejoDraftSaved.set(!state.checkedAt && this.hasCotejoWork());
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.cotejoBusy.set(false);
    }
  }
  cotejoCount(row: Row, article: Article) {
    return this.cotejo()?.counts?.[row.id]?.[article.code] ?? 0;
  }
  cotejoTotal(row: Row) {
    return row.articles.reduce((sum, article) => sum + this.cotejoCount(row, article), 0);
  }
  cotejoMissing(row: Row) {
    return Math.max(0, row.minimum - this.cotejoTotal(row));
  }
  cotejoUnits(state: CotejoState) {
    return Object.values(state.counts ?? {}).reduce((total, group) => total + Object.values(group).reduce((sum, value) => sum + value, 0), 0);
  }
  cotejoCompletedGroups(state = this.cotejo()) {
    if (!state) return 0;
    return this.rows().filter((row) => row.articles.reduce((sum, article) => sum + (state.counts?.[row.id]?.[article.code] ?? 0), 0) >= row.minimum).length;
  }
  cotejoMissingUnits(state = this.cotejo()) {
    if (!state) return this.rows().reduce((sum, row) => sum + row.minimum, 0);
    return this.rows().reduce((sum, row) => {
      const total = row.articles.reduce((n, article) => n + (state.counts?.[row.id]?.[article.code] ?? 0), 0);
      return sum + Math.max(0, row.minimum - total);
    }, 0);
  }
  adjustCotejo(row: Row, article: Article, change: number) {
    if (change > 0 && this.cotejoTotal(row) >= row.minimum) {
      this.overagePrompt.set({ row, article });
      return;
    }
    this.applyCotejoChange(row, article, change);
  }
  confirmCotejoOverage() {
    const pending = this.overagePrompt();
    if (!pending) return;
    this.overagePrompt.set(null);
    this.applyCotejoChange(pending.row, pending.article, 1);
  }
  private applyCotejoChange(row: Row, article: Article, change: number) {
    const state = this.cotejo();
    if (!state) return;
    const wasMissing = this.cotejoMissing(row);
    const counts = structuredClone(state.counts ?? {});
    counts[row.id] ??= {};
    counts[row.id][article.code] = Math.max(0, Math.min(10000, (counts[row.id][article.code] ?? 0) + change));
    if (!counts[row.id][article.code]) delete counts[row.id][article.code];
    this.cotejo.set({ ...state, counts });
    this.cotejoDirty.set(true);
    this.cotejoDraftSaved.set(false);
    if (wasMissing > 0 && this.cotejoMissing(row) === 0) {
      this.cotejoCompleted.set(row.id);
      window.setTimeout(() => {
        if (this.cotejoCompleted() === row.id) this.cotejoCompleted.set(null);
      }, 1300);
    }
  }
  private escapeHtml(value: string) {
    const div = document.createElement('div'); div.textContent = value; return div.innerHTML;
  }
  updateObservation(value: string) {
    this.cotejoObservation = value;
    this.cotejoObservationHtml = this.escapeHtml(value).replaceAll('\n', '<br>');
    this.cotejoDirty.set(true);
    this.cotejoDraftSaved.set(false);
  }
  trustedObservation(html: string): SafeHtml { return this.sanitizer.bypassSecurityTrustHtml(this.cleanObservation(html)); }
  private cleanObservation(html: string) {
    const doc = new DOMParser().parseFromString('<div>' + html + '</div>', 'text/html');
    const allowed = new Set(['DIV','P','BR','B','STRONG','I','EM','U','OL','UL','LI','SPAN']);
    for (const node of Array.from(doc.body.querySelectorAll('*'))) {
      if (node.tagName === 'FONT') {
        const span=doc.createElement('span');span.innerHTML=node.innerHTML;const color=node.getAttribute('color');if(color)span.style.color=color;node.replaceWith(span);continue;
      }
      if (!allowed.has(node.tagName)) { node.replaceWith(...Array.from(node.childNodes)); continue; }
      for (const attr of Array.from(node.attributes)) {
        if (attr.name !== 'style') node.removeAttribute(attr.name);
      }
      if (node.hasAttribute('style')) {
        const color = (node as HTMLElement).style.color;
        const background = (node as HTMLElement).style.backgroundColor;
        node.removeAttribute('style');
        if (color && /^rgba?\([\d\s,.%]+\)$|^#[\da-f]{3,8}$/i.test(color)) (node as HTMLElement).style.color = color;
        if (background && /^rgba?\([\d\s,.%]+\)$|^#[\da-f]{3,8}$/i.test(background)) (node as HTMLElement).style.backgroundColor = background;
      }
    }
    return doc.body.firstElementChild?.innerHTML.slice(0, 20000) ?? '';
  }
  async addCotejoFiles(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (this.cotejoAttachments.length + files.length > 8) { this.error.set('Se permiten hasta 8 adjuntos.'); input.value=''; return; }
    this.error.set('');
    try {
      for (const file of files) {
        if (!file.size) { this.error.set(`${file.name}: el archivo está vacío.`); continue; }
        if (file.size > 2 * 1024 * 1024) { this.error.set(`${file.name}: el máximo por archivo es 2 MB.`); continue; }
        const bytes = new Uint8Array(await file.arrayBuffer());
        this.cotejoAttachments.push({ id: crypto.randomUUID().replaceAll('-',''), name: file.name.slice(0,120), mime: (file.type || 'application/octet-stream').slice(0,100), size: file.size, base64: toBase64(bytes) });
      }
    } catch (e) {
      this.error.set(`No se pudo leer el archivo. ${this.message(e)}`);
    }
    input.value=''; this.cotejoAttachments = [...this.cotejoAttachments];
    if (files.length) { this.cotejoDirty.set(true); this.cotejoDraftSaved.set(false); }
  }
  removeCotejoFile(id: string) { this.cotejoAttachments = this.cotejoAttachments.filter((file) => file.id !== id); this.cotejoDirty.set(true); this.cotejoDraftSaved.set(false); }
  attachmentUrl(file: CotejoAttachment) { return `data:${file.mime || 'application/octet-stream'};base64,${file.base64}`; }
  private openAttachments(title: string, attachments: CotejoAttachment[], selected?: CotejoAttachment) { if (!attachments.length) return; this.attachmentViewer.set({ title, attachments }); this.attachmentPreview.set(selected ?? attachments[0]); }
  viewCurrentAttachments(file?: CotejoAttachment) { this.openAttachments(this.cotejoTitle || 'Sin título', this.cotejoAttachments, file); }
  viewAttachments(item: CotejoState) { if (item.attachments?.length) this.openAttachments(item.title || 'Sin título', item.attachments); }
  closeAttachments() { this.attachmentViewer.set(null); this.attachmentPreview.set(null); }
  scanCotejo() {
    const scanned = this.scanCode().trim();
    const value = scanned.startsWith('$') ? scanned.slice(1).trim() : scanned;
    if (!value) return;
    const matches: { row: Row; article: Article }[] = [];
    for (const row of this.rows()) {
      const article = row.articles.find((a) => a.code === value || a.barcode === value);
      if (article) matches.push({ row, article });
    }
    if (matches.length) {
      for (const match of matches) this.adjustCotejo(match.row, match.article, 1);
      this.cotejoActiveId.set(matches[0].row.id);
      this.activeId.set(matches[0].row.id);
      this.activeArticleCode.set(matches[0].article.code);
      this.scanCode.set('');
      this.error.set('');
      this.scanError.set('');
      this.toast.emit(
        matches.length === 1
          ? `${matches[0].article.code} · unidad añadida al cotejo`
          : `${matches[0].article.code} · unidad añadida a ${matches.length} grupos`,
      );
      return;
    }
    this.scanCode.set('');
    this.scanError.set('El código no pertenece a los artículos del listado actual.');
  }
  async loadPolicy() {
    this.policyBusy.set(true);
    this.error.set('');
    try {
      const state = await this.bridge.call<TenenciaPolicySettings>('tenencia.policy.state');
      this.policy.set({ ...state });
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.policyBusy.set(false);
    }
  }
  setExpiryReviewMonths(value: number | string) {
    const profile = this.policy();
    if (profile) this.policy.set({ ...profile, expiryReviewMonths: Number(value) });
  }
  setCountExpiredStock(value: boolean) {
    const profile = this.policy();
    if (profile) this.policy.set({ ...profile, countExpiredStock: value });
  }
  async savePolicy() {
    const profile = this.policy();
    if (!profile || this.policyBusy()) return;
    this.policyBusy.set(true);
    this.error.set('');
    try {
      const response = await this.bridge.call<TenenciaPolicySettings>('tenencia.policy.save', profile);
      this.policy.set({ ...response });
      this.result.update((result) =>
        result
          ? {
              ...result,
              policy: {
                expiryReviewMonths: response.expiryReviewMonths,
                countExpiredStock: response.countExpiredStock,
              },
            }
          : result,
      );
      this.toast.emit('Configuración de caducidades guardada para toda la farmacia.');
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.policyBusy.set(false);
    }
  }
  async saveCotejo() {
    const state = this.cotejo();
    if (!state || !this.result()?.listVersion || this.cotejoBusy()) return;
    this.cotejoBusy.set(true);
    try {
      const saved = await this.bridge.call<CotejoState>('cotejo.save', {
        ...state,
        listVersion: this.result()!.listVersion,
        observation: this.cotejoObservation,
        observationHtml: this.cotejoObservationHtml,
        title: this.cotejoTitle.trim() || 'Sin título',
        attachments: this.cotejoAttachments,
      });
      this.cotejo.set(saved);
      this.cotejoTitle = saved.title ?? this.cotejoTitle;
      this.cotejoDirty.set(false);
      this.cotejoDraftSaved.set(false);
      this.toast.emit('Cotejo guardado en el historial de la farmacia.');
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.cotejoBusy.set(false);
    }
  }
  async toggleCotejoHistory() {
    if (this.historyOpen()) { this.historyOpen.set(false); return; }
    this.cotejoBusy.set(true);
    try {
      const result = await this.bridge.call<{ rows: CotejoState[] }>('cotejo.history');
      this.cotejoHistory.set(result.rows ?? []);
      this.historyOpen.set(true);
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.cotejoBusy.set(false);
    }
  }
  private cotejoPayload() {
    const state = this.cotejo();
    if (!state || !this.result()?.listVersion) return null;
    return { ...state, listVersion: this.result()!.listVersion, observation: this.cotejoObservation, observationHtml: this.cotejoObservationHtml, title: this.cotejoTitle, attachments: this.cotejoAttachments };
  }
  async saveCotejoDraft() {
    const payload = this.cotejoPayload(); if (!payload) return;
    this.cotejoBusy.set(true);
    try {
      const saved = await this.bridge.call<CotejoState>('cotejo.draft', payload);
      this.cotejo.set(saved); this.cotejoDirty.set(false); this.cotejoDraftSaved.set(true);
      this.toast.emit('Borrador guardado para continuar más tarde.');
      if (this.leaveCotejo()) { await this.clearCotejo(); this.finishCotejoExit(); }
    }
    catch (e) { this.error.set(this.message(e)); }
    finally { this.cotejoBusy.set(false); }
  }
  async clearCotejo() {
    try {
      const empty = await this.bridge.call<CotejoState>('cotejo.clear');
      this.cotejo.set(empty); this.cotejoTitle='Sin título'; this.cotejoObservation=''; this.cotejoObservationHtml=''; this.cotejoAttachments=[]; this.cotejoDirty.set(false); this.cotejoDraftSaved.set(false);
    } catch (e) { this.error.set(this.message(e)); }
  }
  async discardAndLeave() { await this.clearCotejo(); this.finishCotejoExit(); }
  openHistoryItem(item: CotejoState) {
    this.cotejo.set(structuredClone(item)); this.cotejoTitle=item.title?.trim()||'Sin título'; this.cotejoObservation=item.observation??''; this.cotejoObservationHtml=item.observationHtml||this.escapeHtml(item.observation??''); this.cotejoAttachments=[...(item.attachments??[])]; this.cotejoDirty.set(false); this.cotejoDraftSaved.set(!item.checkedAt); this.historyOpen.set(false);
  }
  async deleteHistoryItem() {
    const item=this.historyDelete(); if (!item?.id) return;
    this.cotejoBusy.set(true);
    try { await this.bridge.call('cotejo.delete',{id:item.id}); this.cotejoHistory.update((rows)=>(rows??[]).filter((row)=>row.id!==item.id)); this.historyDelete.set(null); this.toast.emit('Cotejo eliminado del historial.'); }
    catch(e){this.error.set(this.message(e));}
    finally{this.cotejoBusy.set(false);}
  }
  filteredCotejoHistory() {
    const term=this.historySearch().trim().toLocaleLowerCase('es');
    return (this.cotejoHistory()??[]).filter((item)=>{
      const complete=this.cotejoMissingUnits(item)===0;
      const matchesFilter=this.historyFilter()==='all'||(this.historyFilter()==='complete'&&complete)||(this.historyFilter()==='pending'&&!complete)||(this.historyFilter()==='draft'&&!item.checkedAt);
      const haystack=`${item.title??''} ${item.userName??''} ${item.observation??''}`.toLocaleLowerCase('es');
      return matchesFilter&&(!term||haystack.includes(term));
    });
  }
  async exportHistoryItem(item: CotejoState, kind: 'xlsx'|'csv'|'txt'|'doc'|'pdf'|'print') {
    this.reportCotejo.set(item); this.reportMode.set('cotejo'); this.historyOpen.set(false); await this.openReport(kind);
  }
  async printHistoryItem(item: CotejoState) { await this.exportHistoryItem(item,'print'); }
  move(e: Event, step: number) {
    const element = e.currentTarget as HTMLElement;
    const buttons = Array.from(
      element.closest('.group-list')?.querySelectorAll<HTMLButtonElement>('.group-button') ?? [],
    );
    const index = buttons.indexOf(element as HTMLButtonElement);
    const next = buttons[index + step];
    if (next) {
      e.preventDefault();
      next.focus();
      next.click();
    }
  }
  moveArticle(e: Event, step: number) {
    const element = e.currentTarget as HTMLElement;
    const buttons = Array.from(
      element.closest('.tenencia-table')?.querySelectorAll<HTMLButtonElement>('.article-select') ?? [],
    );
    const next = buttons[buttons.indexOf(element as HTMLButtonElement) + step];
    if (next) {
      e.preventDefault();
      next.focus();
      next.click();
    }
  }
  number(n: number | null) {
    return n === null ? '—' : new Intl.NumberFormat('es-ES', { maximumFractionDigits: 3 }).format(n);
  }
  date(s?: string) {
    return s ? new Date(s).toLocaleString('es-ES') : 'Sin consultar';
  }
  shortLabel(s: string) {
    return s === 'not_checked' ? 'Sin consultar' : s === 'covered' ? 'Correcto' : s === 'shortfall' ? 'Falta' : '—';
  }
  label(s: string) {
    return s === 'not_checked' ? 'Sin consultar' : s === 'covered' ? 'Correcto' : s === 'shortfall' ? 'Falta' : '—';
  }
  detail(row: Row) {
    this.previousFocus = document.activeElement as HTMLElement;
    this.selected.set(row);
    setTimeout(() => document.querySelector<HTMLElement>('.tenencia-detail button')?.focus(), 0);
  }
  close() {
    this.selected.set(null);
    if (this.previousFocus?.isConnected) this.previousFocus.focus();
  }
  issue(a: Article) {
    switch (a.issue) {
      case 'not_checked':
        return 'Existencias sin consultar.';
      case 'not_found':
        return 'No dado de alta en Nixfarma: cuenta como 0.';
      case 'stock_not_found':
        return 'No trabajado';
      case 'stock_null':
        return 'Existencias vacías en Nixfarma: cuenta como 0.';
      case 'summed_stock':
        return 'Varias fichas de existencias: se muestra la suma.';
      case 'duplicate_article':
        return 'CN repetido en artículos: se usa la primera descripción.';
      default:
        return '';
    }
  }
  @HostListener('document:keydown.escape') escape() {
    this.close();
  }
  private message(e: unknown) {
    return e instanceof Error ? e.message : 'No se pudo completar la consulta.';
  }
}

export function expiryState(expiry?: string | null, today = new Date(), months = 3): ExpiryState {
  if (!expiry) return 'none';
  const [y, m, d] = expiry.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  if (date < start) return 'expired';
  const limit = new Date(start);
  limit.setMonth(limit.getMonth() + months);
  return date <= limit ? 'soon' : 'ok';
}
/** Selección de artículos visibles (ver visibleArticles). */
export function pickVisible<
  T extends { code: string; stock: number | null; lastPurchase?: string | null; lastSale?: string | null },
>(sorted: T[], movements: boolean): T[] {
  const recent = (a: T) => [a.lastPurchase ?? '', a.lastSale ?? ''].sort().pop() ?? '';
  const keep = new Set(sorted.filter((a) => (a.stock ?? 0) > 0).map((a) => a.code));
  const rest = sorted.filter((a) => !keep.has(a.code));
  // Sin existencias leídas (antes de consultar) no hay movimientos: se muestran los 10 primeros.
  const read = sorted.some((a) => a.stock !== null);
  const candidates =
    movements && read ? rest.filter((a) => recent(a)).sort((a, b) => recent(b).localeCompare(recent(a))) : rest;
  for (const a of candidates) {
    if (keep.size >= VISIBLE_ARTICLES) break;
    keep.add(a.code);
  }
  return sorted.filter((a) => keep.has(a.code));
}
