import {Component,computed,inject,signal,output,OnInit,HostListener} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {DesktopBridge} from '../../bridge';
import {IconComponent} from '../../icon';
interface Article{code:string;description:string;stock:number|null;issue?:string}
interface Row{id:string;name:string;minimum:number;stock:number|null;missing:number|null;status:string;articles:Article[]}
interface Result{code:string;message:string;province:string;provinceName?:string;listVersion?:string;source?:string;checkedAt?:string;rows?:Row[]}
@Component({selector:'viteka-tenencia',standalone:true,imports:[FormsModule,IconComponent],templateUrl:'./tenencia.html',styleUrl:'./tenencia.css'})
export class TenenciaComponent implements OnInit{
 private bridge=inject(DesktopBridge);readonly back=output<void>();readonly busy=signal(false);readonly result=signal<Result|null>(null);readonly error=signal('');readonly search=signal('');readonly status=signal('all');readonly current=signal(1);readonly selected=signal<Row|null>(null);private previousFocus:HTMLElement|null=null;
 readonly rows=computed(()=>this.result()?.rows??[]);readonly covered=computed(()=>this.rows().filter(r=>r.status==='covered').length);readonly shortfalls=computed(()=>this.rows().filter(r=>r.status==='shortfall').length);readonly unresolved=computed(()=>this.rows().filter(r=>r.status==='unresolved').length);
 readonly filtered=computed(()=>{const q=this.search().trim().toLocaleLowerCase('es');return this.rows().filter(r=>(this.status()==='all'||r.status===this.status())&&(!q||r.name.toLocaleLowerCase('es').includes(q)||r.articles.some(a=>a.code.includes(q)||a.description.toLocaleLowerCase('es').includes(q))));});
 readonly pages=computed(()=>Math.max(1,Math.ceil(this.filtered().length/25)));readonly visible=computed(()=>this.filtered().slice((Math.min(this.current(),this.pages())-1)*25,Math.min(this.current(),this.pages())*25));readonly canQuery=computed(()=>!!this.result()?.listVersion&&!this.busy());
 async ngOnInit(){await this.state();}
 async state(){this.busy.set(true);try{this.result.set(await this.bridge.call<Result>('tenencia.state'));}catch(e){this.error.set(this.message(e));}finally{this.busy.set(false);}}
 async query(){if(!this.canQuery())return;this.busy.set(true);this.error.set('');this.result.update(r=>r?{...r,rows:undefined,checkedAt:undefined}:r);this.selected.set(null);this.current.set(1);try{this.result.set(await this.bridge.call<Result>('tenencia.query'));}catch(e){this.error.set(this.message(e));}finally{this.busy.set(false);}}
 filter(){this.current.set(1);}number(n:number|null){return n===null?'Pendiente':new Intl.NumberFormat('es-ES',{maximumFractionDigits:3}).format(n);}date(s?:string){return s?new Date(s).toLocaleString('es-ES'):'Sin consultar';}
 label(s:string){return s==='covered'?'Cubierto':s==='shortfall'?'Faltan unidades':'Pendiente de revisar';}
 detail(row:Row){this.previousFocus=document.activeElement as HTMLElement;this.selected.set(row);setTimeout(()=>document.querySelector<HTMLElement>('.tenencia-detail button')?.focus(),0);}
 close(){this.selected.set(null);if(this.previousFocus?.isConnected)this.previousFocus.focus();}
 issue(a:Article){return a.issue==='ambiguous_stock'?'Varias filas de existencias: hay que concretar el almacén.':a.issue==='duplicate_article'?'Código de artículo duplicado.':a.stock===null?'Sin un dato de existencias inequívoco.':'';}
 @HostListener('document:keydown.escape') escape(){this.close();}
 private message(e:unknown){return e instanceof Error?e.message:'No se pudo completar la consulta.';}
}
