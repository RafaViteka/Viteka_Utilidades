import { Component, computed, inject, signal, output, input, effect, OnInit, HostListener } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DesktopBridge } from '../../bridge';
import { IconComponent } from '../../icon';
type GroupSort = 'classification' | 'name' | 'minimum' | 'status';
type ArticleSort = 'code' | 'description' | 'stock';
interface Article {
  code: string;
  description: string;
  stock: number | null;
  issue?: string;
}
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
}
@Component({
  selector: 'viteka-tenencia',
  standalone: true,
  imports: [FormsModule, IconComponent],
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
  readonly checkedTime = computed(() => {
    const at = this.result()?.checkedAt;
    return at ? new Date(at).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }) : '';
  });
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
  exportShortfalls() {
    const cell = (v: unknown) => {
      const text = v === null || v === undefined ? '' : String(v);
      return /[;"\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
    };
    const lines = [['Código', 'Grupo', 'Mínimo', 'Existencias', 'Faltan', 'CN', 'Medicamento', 'Existencias CN']];
    for (const r of this.shortfallRows()) {
      if (!r.articles.length)
        lines.push([r.classification ?? '', r.name, r.minimum, r.stock, r.missing, '', '', ''] as never);
      for (const a of this.sortedArticles(r))
        lines.push([
          r.classification ?? '',
          r.name,
          r.minimum,
          r.stock,
          r.missing,
          a.code,
          a.description,
          a.stock ?? 0,
        ] as never);
    }
    const csv = '\ufeff' + lines.map((l) => l.map(cell).join(';')).join('\r\n');
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');
    const day = (this.result()?.checkedAt ?? new Date().toISOString()).slice(0, 10);
    link.href = url;
    link.download = `tenencia-faltas-${day}.csv`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  print() {
    window.print();
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
      this.restoreGroup();
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
      if (r.code === 'ok' && r.checkedAt)
        this.summary.emit({
          total: this.rows().length,
          covered: this.covered(),
          shortfall: this.shortfalls(),
          checkedAt: r.checkedAt,
        });
      if (r.code !== 'ok') this.status.set('all');
    } catch (e) {
      this.status.set('all');
      this.error.set(this.message(e));
    } finally {
      this.busy.set(false);
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
        return 'No dado de alta en NixFarma: cuenta como 0.';
      case 'stock_not_found':
        return 'Sin ficha de existencias en NixFarma: cuenta como 0.';
      case 'stock_null':
        return 'Existencias vacías en NixFarma: cuenta como 0.';
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
