"""Regression against the actual Windows ODBC Driver Manager, no Oracle or live DB."""
import importlib.util
from pathlib import Path
import sys
import uuid
import winreg
import pyodbc

spec=importlib.util.spec_from_file_location('bridge',Path(__file__).with_name('server.py'))
bridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)
assert sys.platform=='win32'
drivers=pyodbc.drivers()
driver=next((d for d in drivers if 'SQL Server' in d),None)
assert driver, 'Windows runner needs its installed SQL Server ODBC driver for the Driver Manager regression'
with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r'SOFTWARE\ODBC\ODBCINST.INI' + chr(92) + driver) as key:
    dll=winreg.QueryValueEx(key,'Driver')[0]
name='VitekaMCP'+uuid.uuid4().hex[:12]
root=r'Software\ODBC\ODBC.INI'
try:
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,root+'\\'+name) as key:
        for keyname,value in [('Driver',dll),('Server','127.0.0.1,1'),('Database','master')]:
            winreg.SetValueEx(key,keyname,0,winreg.REG_SZ,value)
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,root+r'\ODBC Data Sources') as key:
        winreg.SetValueEx(key,name,0,winreg.REG_SZ,driver)
    assert name in pyodbc.dataSources()
    profile={'dsn':name,'user':'nonexistent-test-user','password':'nonexistent-test-password'}
    def state(connection_string):
        try:
            conn=pyodbc.connect(connection_string,timeout=1)
            conn.close()
            raise AssertionError('Unexpected database at loopback port1')
        except pyodbc.Error as error:
            return error.args[0]
    old=';'.join(k+'='+bridge.odbc_value(profile[v]) for k,v in [('DSN','dsn'),('UID','user'),('PWD','password')])
    legacy=state(old)
    fixed=state(bridge.connection_string(profile))
    print('Driver Manager regression: old DSN state=',legacy,'; corrected DSN state=',fixed)
    assert legacy=='IM002', 'Expected old braced DSN to fail name resolution'
    assert fixed not in ('IM002','IM003','IM014'), 'Corrected DSN must reach the installed driver, not fail DSN resolution'
finally:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,root+r'\ODBC Data Sources',0,winreg.KEY_SET_VALUE) as key:
        winreg.DeleteValue(key,name)
    winreg.DeleteKey(winreg.HKEY_CURRENT_USER,root+'\\'+name)
print('PASS: corrected DSN reaches driver; all temporary CI registry entries removed')
