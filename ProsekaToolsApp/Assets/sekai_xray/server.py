# run this file with the following command:
# pip install loguru msgspec msgpack mitmproxy
# mitmweb --mode wireguard -s parse.py --set ignore_hosts=icloud.com.cn --set ignore_hosts=apple.com

# Fill these two thing first with format like: b'put_string_here' 
AES_KEY = b"THE_KEY"
AES_IV = b"THE_IV"

# You don't need to modify the following code if you don't care about it.
assert AES_KEY != b'THE_KEY', "Please find and fill the AES_KEY by yourself!"
assert AES_KEY != b'THE_IV', "Please find and fill the AES_IV by yourself!"

import asyncio
import base64
import glob
import io
import json
import sys
import textwrap
from subprocess import CREATE_NEW_CONSOLE, PIPE, Popen
import time
from typing import List, Optional

import mitmproxy.dns
import mitmproxy.http
import mitmproxy.tcp
import mitmproxy.tools.web.app
import mitmproxy.udp
import msgspec
import tornado
from Crypto.Cipher import AES
from Crypto.Util.Padding import pad, unpad
from loguru import logger
from mitmproxy import options
from mitmproxy.tools import web
from mitmproxy.tools.web.app import handlers
from msgpack import packb, unpackb
from msgspec import Struct as BaseModel


class GridSize(BaseModel):
    width: int
    depth: int
    height: int

class MysekaiFixtureTagGroup(BaseModel):
    id: int
    mysekaiFixtureTagId1: int
    mysekaiFixtureTagId2: Optional[int] = None
    mysekaiFixtureTagId3: Optional[int] = None

class ModelItem(BaseModel, kw_only=True):
    id: int
    mysekaiFixtureType: str
    name: str
    pronunciation: str
    flavorText: str
    seq: int
    gridSize: GridSize
    mysekaiFixtureMainGenreId: Optional[int] = None
    mysekaiFixtureSubGenreId: Optional[int] = None
    mysekaiFixtureHandleType: str
    mysekaiSettableSiteType: str
    mysekaiSettableLayoutType: str
    mysekaiFixturePutType: str
    mysekaiFixtureAnotherColors: List
    mysekaiFixturePutSoundId: int
    mysekaiFixtureFootstepId: Optional[int] = None
    mysekaiFixtureTagGroup: Optional[MysekaiFixtureTagGroup] = None
    isAssembled: bool
    isDisassembled: bool
    mysekaiFixturePlayerActionType: str
    isGameCharacterAction: bool
    assetbundleName: str

class UserMysekaiSiteHarvestFixture(BaseModel):
    mysekaiSiteHarvestFixtureId: int
    positionX: int
    positionZ: int
    hp: int
    userMysekaiSiteHarvestFixtureStatus: str

class UserMysekaiSiteHarvestResourceDrop(BaseModel):
    resourceType: str
    resourceId: int
    positionX: int
    positionZ: int
    hp: int
    seq: int
    mysekaiSiteHarvestResourceDropStatus: str
    quantity: int

class Map(BaseModel, kw_only=True):
    mysekaiSiteId: int
    siteName: Optional[str] = None
    userMysekaiSiteHarvestFixtures: List[UserMysekaiSiteHarvestFixture]
    userMysekaiSiteHarvestResourceDrops: List[UserMysekaiSiteHarvestResourceDrop]


SITE_ID = {
    1: "マイホーム",
    2: "1F",
    3: "2F",
    4: "3F",
    5: "さいしょの原っぱ",
    6: "願いの砂浜",
    7: "彩りの花畑",
    8: "忘れ去られた場所",
}

HARVEST_MAP = {}
LAST_UPDATE_TIME = time.time()

def parse_map(user_data: dict):
    assert user_data["updatedResources"]["userMysekaiHarvestMaps"]

    harvest_maps: List[Map] = [ 
        msgspec.json.decode(msgspec.json.encode(mp), type=Map) for mp in user_data["updatedResources"]["userMysekaiHarvestMaps"]
    ]

    for mp in harvest_maps:
        mp.siteName = SITE_ID[mp.mysekaiSiteId]

    processed_map = {}
    for mp in harvest_maps:
        print(f"Site: {mp.siteName}")
        mp_detail = []
        for fixture in mp.userMysekaiSiteHarvestFixtures:
            #  spawned
            #  harvested
            if fixture.userMysekaiSiteHarvestFixtureStatus == "spawned":
                mp_detail.append( 
                    {
                        "location": (fixture.positionX, fixture.positionZ),
                        "fixtureId": fixture.mysekaiSiteHarvestFixtureId,
                        "reward": {}
                    }
                )
            
        for drop in mp.userMysekaiSiteHarvestResourceDrops:
            pos = (drop.positionX, drop.positionZ)
            for i in range(0, len(mp_detail)):
                if mp_detail[i]["location"] != pos:
                    continue
                
                # mysekai_material
                # mysekai_item
                # mysekai_fixture
                # mysekai_music_record
                mp_detail[i]["reward"].setdefault(drop.resourceType, {})
                mp_detail[i]["reward"][drop.resourceType][drop.resourceId] = \
                    mp_detail[i]["reward"][drop.resourceType].get(drop.resourceId, 0) + drop.quantity
                break
        
        processed_map[mp.siteName] = mp_detail
    
    return processed_map


def unmsgpack(data: bytes) -> dict:
    return unpackb(data, strict_map_key=False) if len(data) > 0 else {}

def decrypt(ciphertext: bytes, key: bytes, iv: bytes) -> bytes:
    cipher = AES.new(key, AES.MODE_CBC, iv=iv)
    plaintext: bytes = unpad(cipher.decrypt(ciphertext), 16)
    return plaintext

def encrypt(plaintext: bytes, key: bytes, iv: bytes) -> bytes:
    cipher = AES.new(key, AES.MODE_CBC, iv=iv)
    ciphertext: bytes = cipher.encrypt(pad(plaintext, 16))
    return ciphertext

class Inspector:
    def __init__(self):
        logger.remove()
        self.log = logger.opt(colors=True)
        self.raw_log = logger
        self.process = Popen([
            sys.executable, "-c", textwrap.dedent("""
                import sys
        
                for bin in sys.stdin.buffer:
                    print(bin.decode('utf-8'), end='')
                """)],
            stdin = PIPE, 
            creationflags = CREATE_NEW_CONSOLE
        )
        self.io = io.TextIOWrapper(self.process.stdin, encoding='utf-8')
        logger.add(self.io, colorize=True, format="<green>{time:HH:mm:ss.SSSSSS}</green> <level>{message}</level>")
        print("mitmproxy console started.")
        logger.info("Raw logger started.")

    def done(self):
        self.log.remove()

    def response(self, flow: mitmproxy.http.HTTPFlow):
        print(flow.request.host_header)
        if flow.request.url.find("isForceAllReloadOnlyMysekai") == -1:
            return
        
        assert flow.response is not None
        assert flow.response.content is not None
        assert flow.request.content is not None

        self.log.info(f"<blue><b>[HTTP]</b></blue> <fg 128,128,128><b>{flow.request.method}</b></fg 128,128,128>: <C> {flow.request.url} </C>")
        self.log.info(f"| Request Raw: {flow.request.content[:100]}")
        try:
            req_decrypted = unmsgpack(decrypt(flow.request.content, AES_KEY, AES_IV))
            self.log.info(f"| Request Decrypted: {req_decrypted}")
        except:
            req_decrypted = base64.b64encode(flow.request.content).decode()
            self.log.info(f"| Unable to decrypted Request : {req_decrypted}")
        
        
        self.raw_log.info(f"| Response Raw: {flow.response.content[:100]}")
        try:
            res_decrypted = unmsgpack(decrypt(flow.response.content, AES_KEY, AES_IV))
            self.raw_log.info(f"| Response Decrypted: {str(res_decrypted)[:300]}")
        except:
            res_decrypted = base64.b64encode(flow.response.content).decode()
            self.raw_log.info(f"| Unable to decrypted Response: {str(res_decrypted)[:300]}")
            return

        mysekai_info = res_decrypted
        self.raw_log.info(str(mysekai_info.keys()))
        if "updatedResources" not in mysekai_info.keys() or \
            "userMysekaiHarvestMaps" not in mysekai_info["updatedResources"].keys():
            return
        
        self.raw_log.info("| Find Harvest Maps Info")
        result = parse_map(mysekai_info)
        for k, v in result.items():
            self.raw_log.info(f"| Site: {k} \n {json.dumps(v)}")
            global HARVEST_MAP, LAST_UPDATE_TIME
            HARVEST_MAP[k] = v
            LAST_UPDATE_TIME = time.time()
        

        
# addons = [
#     Inspector()
# ]


class MySekaiHandler(tornado.web.RequestHandler):
    def set_default_headers(self):
        self.set_header("Access-Control-Allow-Origin", "*") 
        self.set_header("Access-Control-Allow-Methods", "POST, GET, OPTIONS, PUT, DELETE")
        self.set_header("Access-Control-Allow-Headers", "Content-Type, X-Requested-With, X-Custom-Header")

    def options(self, *args, **kwargs):
        self.set_status(204) # 204 No Content
        self.finish()

    async def get(self):
        self.write(json.dumps({
            "last_update_time": LAST_UPDATE_TIME,
            "harvest_map": HARVEST_MAP
        }))

mitmproxy.tools.web.app.handlers.append((r"/mysekai", MySekaiHandler))

async def start_proxy(host, port):
    opts = options.Options(listen_port=7763, ignore_hosts=["icloud.com.cn"], mode=["wireguard"])

    master = web.master.WebMaster(
        opts,
        with_termlog=True,
    )
    master.addons.add(Inspector())

    opts.add_option("web_port", int, port, "Web UI port.")
    opts.add_option("web_host", str, host, "Web UI host.")
    print(f"Starting mitmweb api on {host}:{port}")
    await master.run()

if __name__ == '__main__':
    host="127.0.0.1"
    port=12743
    asyncio.run(start_proxy(host, port))