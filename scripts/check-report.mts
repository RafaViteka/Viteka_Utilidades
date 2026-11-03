import assert from 'node:assert/strict';
import {purchaseCandidates,csvCell} from '../frontend/src/apps/tenencia/tenencia-report.ts';
const articles=Array.from({length:20},(_,i)=>({code:String(650000+i),description:'Artículo '+i,stock:i===19?50:0,lastPurchase:i<12?`2026-10-${String(i+1).padStart(2,'0')}`:null,lastSale:'2099-12-31'}));
const before=JSON.stringify(articles),picked=purchaseCandidates(articles);
assert.equal(picked.length,10);assert.equal(picked[0].code,'650011');assert.equal(picked[9].code,'650002');assert(!picked.some(a=>a.code==='650019'));assert.equal(JSON.stringify(articles),before);
assert.deepEqual(purchaseCandidates(articles.slice(10)).map(a=>a.code),['650011','650010',...articles.slice(12).map(a=>a.code)]);
assert.equal(csvCell('=1+1'),"'=1+1");assert.equal(csvCell('031647'),'031647');assert.equal(csvCell('A;B'),'"A;B"');
console.log('PASS: ten purchase-only candidates; missing dates after known dates; no sale fallback; stable CN ties; input unchanged; safe CSV.');
