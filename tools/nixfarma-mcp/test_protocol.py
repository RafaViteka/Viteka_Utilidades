import asyncio
from pathlib import Path
import sys
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

async def main():
    params=StdioServerParameters(command=sys.executable,args=[str(Path(__file__).with_name('server.py'))])
    async with stdio_client(params) as (reader,writer):
        async with ClientSession(reader,writer) as session:
            await session.initialize()
            tools=await session.list_tools()
            assert {t.name for t in tools.tools}=={'probar_conexion','listar_tablas','describir_tabla','consultar_cn','buscar_articulos_por_atc'}
            result=await session.call_tool('consultar_cn',{'cn':"650001' OR 1=1"})
            assert result.isError
    print('PASS: MCP STDIO initialize, five tools, unsafe CN rejected without connecting to Oracle.')

asyncio.run(main())
