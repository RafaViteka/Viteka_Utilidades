import { Component, computed, inject, signal, output, input, effect, OnInit, HostListener } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
  issue?: string;
  expiry?: string | null;
  lastPurchase?: string | null;
  lastSale?: string | null;
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
  cached?: boolean;
  customized?: boolean;
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
  readonly back = output<void>();
  readonly summary = output<{ total: number; covered: number; shortfall: number; checkedAt: string }>();
  /** Identificador del usuario para recordar el último grupo consultado. */
  readonly userKey = input('');
  /** Búsqueda inicial (desde la paleta Ctrl+K). */
  readonly initialSearch = input('');
  /** Solo el superadministrador (Viteka) puede editar el listado. */
  readonly canConfigure = input(false);
  /** Mensajes breves para el aviso flotante de la aplicación. */
  readonly toast = output<string>();
  readonly VISIBLE = VISIBLE_ARTICLES;
  readonly editing = signal(false);
  readonly exporting = signal(false);
  readonly expanded = signal<string[]>([]);
  readonly showExpiry = computed(() => !!this.result()?.fields?.expiry);
  readonly totalMissing = computed(() =>
    this.rows().reduce((n, r) => n + (r.status === 'shortfall' ? (r.missing ?? 0) : 0), 0),
  );
  readonly busy = signal(false);
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
  readonly covered = computed(() => this.rows().filter((r) => r.status === 'covered').length);
  readonly shortfalls = computed(() => this.rows().filter((r) => r.status === 'shortfall').length);
  readonly filtered = computed(() => {
    const q = this.search().trim().toLocaleLowerCase('es');
    return this.rows()
      .filter(
        (r) =>
          (this.status() === 'all' || r.status === this.status()) &&
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
    () =>
      this.activeRow()?.articles.find((a) => a.code === this.activeArticleCode()) ??
      this.activeRow()?.articles[0] ??
      null,
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
  readonly reportMode = signal<'complete' | 'shortfalls'>('shortfalls');
  readonly reportKind = signal<'xlsx' | 'csv' | 'pdf' | 'print'>('xlsx');
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
  readonly reportRows = computed(() =>
    this.reportMode() === 'shortfalls'
      ? this.shortfallsByMissing()
      : this.rows().map((row) => {
          const group = this.declarationGroups().find((g) => g.id === row.id);
          return group
            ? {
                ...row,
                articles: group.codes.map(
                  (code) =>
                    row.articles.find((a) => a.code === code) ?? {
                      code,
                      description: group.descriptions?.[code] ?? 'CN ' + code,
                      stock: 0,
                    },
                ),
              }
            : row;
        }),
  );
  readonly declarationRow = computed(
    () => this.reportRows().find((r) => r.id === this.declarationGroupId()) ?? this.reportRows()[0],
  );
  reportArticles(row: Row) {
    return this.reportMode() === 'shortfalls' ? purchaseCandidates(row.articles) : row.articles;
  }
  reportTitle() {
    return this.reportMode() === 'shortfalls'
      ? 'Tenencia obligatoria · Solo faltas'
      : 'Tenencia obligatoria · Listado completo para declaración';
  }
  reportCriteria() {
    return this.reportMode() === 'shortfalls'
      ? 'Máximo 10 artículos por grupo; última compra descendente. Sin fecha de compra al final, ordenados por CN. No se utilizan ventas.'
      : 'Todos los grupos y candidatos; selección de declaración independiente del catálogo. No acredita automáticamente cumplimiento normativo.';
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
  async openReport(kind: 'xlsx' | 'csv' | 'pdf' | 'print') {
    this.closeMenus();
    this.reportKind.set(kind);
    this.error.set('');
    try {
      const d = await this.bridge.call<{
        listVersion: string;
        groups: {
          id: string;
          name: string;
          minimum: number;
          codes: string[];
          descriptions?: Record<string, string>;
          atcPrefix?: string;
          classificationCode?: string;
        }[];
        selections: Record<string, string[]>;
        reviewRequired: boolean;
      }>('declaration.state');
      if (d.listVersion !== this.result()?.listVersion)
        throw new Error('El listado cambió. Actualiza la consulta antes de exportar.');
      this.declarationGroups.set(d.groups);
      this.declarationPicks.set(d.selections);
      this.declarationReview.set(d.reviewRequired);
      this.reportOpen.set(true);
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  private reportLines(): unknown[][] {
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
        'Seleccionado para declaración',
      ],
    ];
    for (const r of this.reportRows()) {
      const articles = this.reportArticles(r),
        base = [r.classification ?? '', r.name, r.minimum, r.stock ?? 0, r.missing ?? 0];
      if (!articles.length) lines.push([...base, '', '', '', '', '']);
      for (const a of articles)
        lines.push([
          ...base,
          a.code,
          a.description,
          a.stock ?? 0,
          a.lastPurchase ?? 'Sin fecha de compra',
          this.reportMode() === 'complete' ? (this.chosenForDeclaration(r, a) ? 'Sí' : 'No') : '',
        ]);
    }
    return lines;
  }
  async generateReport() {
    if (!this.hasStock() || this.exporting()) return;
    return this.run(async () => {
      if (this.reportMode() === 'complete')
        await this.bridge.call('declaration.save', {
          listVersion: this.result()?.listVersion,
          selections: this.declarationPicks(),
        });
      this.reportOpen.set(false);
      const kind = this.reportKind(),
        name = 'tenencia-' + (this.reportMode() === 'shortfalls' ? 'faltas' : 'declaracion') + '-' + this.fileDay();
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
                name: this.reportMode() === 'shortfalls' ? 'Faltas' : 'Declaración',
                widths: [12, 44, 9, 13, 9, 10, 50, 12, 16, 18],
                rows,
                merges: ['A4:J4', 'A5:J5', 'A6:J6'],
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
  /** Caducidad solo para artículos con existencias: verde > 6 meses, amarillo ≤ 6 meses, rojo caducado, gris sin fecha. */
  expiryState(a: Article): ExpiryState | null {
    if (!((a.stock ?? 0) > 0)) return null;
    return expiryState(a.expiry);
  }
  expiryText(a: Article) {
    if (!a.expiry) return 'Sin fecha';
    const [y, m] = a.expiry.split('-');
    return `${m}/${y}`;
  }
  expiryTitle(a: Article) {
    const state = this.expiryState(a);
    return state === 'ok'
      ? 'Caducidad correcta: más de 6 meses'
      : state === 'soon'
        ? 'Caduca en 6 meses o menos'
        : state === 'expired'
          ? 'Caducado'
          : 'Sin fecha de caducidad en la ficha';
  }
  movementDate(s?: string | null) {
    if (!s) return '—';
    const [y, m, d] = s.split('-');
    return `${d}/${m}/${y}`;
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
    this.error.set('');
    this.result.update((r) =>
      r
        ? {
            ...r,
            rows: r.rows?.map((row) => ({
              ...row,
              stock: null,
              missing: null,
              status: 'not_checked',
              articles: row.articles.map((a) => ({ ...a, stock: null, issue: 'not_checked' })),
            })),
            checkedAt: undefined,
          }
        : r,
    );
    this.selected.set(null);
    try {
      const r = await this.bridge.call<Result>('tenencia.query');
      this.result.set(r);
      this.emitSummary(r);
      if (r.code === 'ok') this.toast.emit('Consulta completada · ' + this.stamp(r.checkedAt));
      if (r.code !== 'ok') this.status.set('all');
    } catch (e) {
      this.status.set('all');
      this.error.set(this.message(e));
    } finally {
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
        return 'Sin ficha de existencias en Nixfarma: cuenta como 0.';
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

export function expiryState(expiry?: string | null, today = new Date()): ExpiryState {
  if (!expiry) return 'none';
  const [y, m, d] = expiry.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  if (date < start) return 'expired';
  const limit = new Date(start);
  limit.setMonth(limit.getMonth() + 6);
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
