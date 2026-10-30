import { Component, computed, inject, signal, output, OnInit, HostListener } from '@angular/core';
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
  readonly articleSort = signal<ArticleSort | null>(null);
  readonly articleDescending = signal(false);
  private collator = new Intl.Collator('es', { numeric: true, sensitivity: 'base' });
  readonly hasStock = computed(() => this.result()?.code === 'ok' && !!this.result()?.checkedAt);
  readonly rows = computed(() => this.result()?.rows ?? []);
  readonly covered = computed(() => this.rows().filter((r) => r.status === 'covered').length);
  readonly shortfalls = computed(() => this.rows().filter((r) => r.status === 'shortfall').length);
  readonly unresolved = computed(() => this.rows().filter((r) => r.status === 'unresolved').length);
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
      return (
        (typeof x === 'number' && typeof y === 'number' ? x - y : this.collator.compare(String(x), String(y))) *
        (this.articleDescending() ? -1 : 1)
      );
    });
  }
  reason(row: Row) {
    if (row.status !== 'unresolved') return '';
    if (row.requiresReview) return row.selectionNote || 'Este grupo requiere revisar sus condiciones de presentación.';
    if (!row.articles.length) return 'No hay referencias asociadas para evaluar el grupo.';
    const count = row.articles.filter((a) => a.stock === null || (a.issue && a.issue !== 'not_checked')).length;
    return (
      'No se puede calcular el total: ' +
      count +
      ' de ' +
      row.articles.length +
      ' artículos tienen existencias sin confirmar. Pendiente no significa cero.'
    );
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
        this.search.set('');
      }
      this.status.set('all');
      this.result.set(r);
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
    return n === null ? 'Pendiente' : new Intl.NumberFormat('es-ES', { maximumFractionDigits: 3 }).format(n);
  }
  date(s?: string) {
    return s ? new Date(s).toLocaleString('es-ES') : 'Sin consultar';
  }
  shortLabel(s: string) {
    return s === 'not_checked'
      ? 'Sin consultar'
      : s === 'covered'
        ? 'Cubierto'
        : s === 'shortfall'
          ? 'Faltas'
          : 'Revisar';
  }
  label(s: string) {
    return s === 'not_checked'
      ? 'Sin consultar'
      : s === 'covered'
        ? 'Cubierto'
        : s === 'shortfall'
          ? 'Faltan unidades'
          : 'Pendiente de revisar';
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
    return a.issue === 'not_checked'
      ? 'Existencias sin consultar.'
      : a.issue === 'ambiguous_stock'
        ? 'Varias filas de existencias: hay que concretar el almacén.'
        : a.issue === 'duplicate_article'
          ? 'Código de artículo duplicado: hay que concretar empresa o clase.'
          : a.issue === 'not_found'
            ? 'CN no encontrado en la tabla de artículos.'
            : a.issue === 'stock_not_found'
              ? 'No hay una fila de existencias para este CN.'
              : a.issue === 'stock_null'
                ? 'La fila de existencias no contiene un stock informado.'
                : a.stock === null
                  ? 'Sin un dato de existencias inequívoco.'
                  : '';
  }
  @HostListener('document:keydown.escape') escape() {
    this.close();
  }
  private message(e: unknown) {
    return e instanceof Error ? e.message : 'No se pudo completar la consulta.';
  }
}
