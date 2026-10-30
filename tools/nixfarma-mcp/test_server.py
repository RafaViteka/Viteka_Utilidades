import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('bridge',Path(__file__).with_name('server.py'))
bridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)

class Cursor:
    def __init__(self,fail=False):self.steps=[];self.fail=fail
    def execute(self,sql,*args):
        self.steps.append((sql,args))
        if self.fail and sql=='SET TRANSACTION READ ONLY':raise bridge.pyodbc.Error('42000','ORA-01453 secret password')
        return self
    def fetchone(self):return (1,)
class Connection:
    def __init__(self,fail=False):self.cur=Cursor(fail);self.closed=False;self.rolled=False
    def cursor(self):return self.cur
    def rollback(self):self.rolled=True
    def close(self):self.closed=True
class Tests(unittest.TestCase):
    settings={'dsn':'NIXFARMA','user':'reader','password':'a};PWD=secret'}
    def test_transaction_rollback_and_odbc_escaping(self):
        c=Connection()
        with patch.object(bridge.pyodbc,'connect',return_value=c) as connect:
            with bridge.read_session(self.settings) as cur:cur.execute('SELECT 1 FROM DUAL')
        self.assertEqual(c.cur.steps[0][0],'SET TRANSACTION READ ONLY');self.assertTrue(c.rolled and c.closed)
        self.assertTrue(connect.call_args.args[0].startswith('DSN=NIXFARMA;'));self.assertNotIn('DSN={',connect.call_args.args[0]);self.assertFalse(connect.call_args.kwargs['autocommit']);self.assertIn('PWD={a}};PWD=secret}',connect.call_args.args[0])
    def test_readonly_failure_stops_query_and_sanitizes_error(self):
        c=Connection(True)
        with patch.object(bridge.pyodbc,'connect',return_value=c):
            with self.assertRaisesRegex(RuntimeError,'ORA-01453') as caught:
                with bridge.read_session(self.settings) as cur:cur.execute('SELECT 1 FROM DUAL')
        self.assertNotIn('secret',str(caught.exception));self.assertEqual(len(c.cur.steps),1);self.assertTrue(c.rolled and c.closed)
    def test_cleanup_on_read_error(self):
        c=Connection()
        with patch.object(bridge.pyodbc,'connect',return_value=c):
            with self.assertRaises(ValueError):
                with bridge.read_session(self.settings):raise ValueError('fixture')
        self.assertTrue(c.rolled and c.closed);self.assertFalse(bridge.GATE.locked())
    def test_reject_untrusted_inputs_before_connection(self):
        with patch.object(bridge.pyodbc,'connect') as connect:
            for call in [lambda:bridge.consultar_cn("123456' OR 1=1"),lambda:bridge.describir_tabla('SYS','USERS'),lambda:bridge.describir_tabla('APPUL','AB_ARTICULOS;DROP'),lambda:bridge.buscar_articulos_por_atc('N02%',3),lambda:bridge.buscar_articulos_por_atc('N02BE01',3),lambda:bridge.buscar_articulos_por_atc('N02BE01',5,10000)]:
                with self.assertRaises(ValueError):call()
            connect.assert_not_called()
    def test_dsn_cannot_inject_connection_attributes(self):
        with patch.object(bridge.pyodbc,'connect') as connect:
            with self.assertRaises(ValueError):
                with bridge.read_session({**self.settings,'dsn':'NIXFARMA;UID=admin'}):pass
            connect.assert_not_called()
    def test_busy_is_bounded(self):
        with bridge.GATE:
            with self.assertRaisesRegex(RuntimeError,'curso'):
                with bridge.read_session(self.settings):pass
    def test_cn_and_group_queries_bind_values(self):
        c=Connection();calls=[]
        def fake_rows(cur,sql,parameters=(),limit=100):
            calls.append((sql,parameters))
            if 'FROM APPUL.AB_ARTICULOS WHERE' in sql:return {'filas':[{'GTE_CODIGO':'N02BE01'}],'truncado':False}
            return {'filas':[],'truncado':False}
        with patch.object(bridge,'profile',return_value=self.settings),patch.object(bridge.pyodbc,'connect',return_value=c),patch.object(bridge,'rows',side_effect=fake_rows):r=bridge.consultar_cn('650001')
        self.assertEqual(calls[0][1],('650001',));self.assertNotIn('650001',calls[0][0]);self.assertEqual(calls[1][1],['N02BE01']);self.assertIn('grupos_botplus',r)

if __name__=='__main__':unittest.main()
