# pjsk-mysekai-xray
Online:  
Usage: run `parse.py` in mitmproxy (fill the necessary thing first) -> input json dump into web -> item mapping  
Site: https://middlered.github.io/pjsk-mysekai-xray/paint.html  

For offline we have easier usage:
Run `python3 server.py` -> open `paint_local.html` -> auto syncing 

# Manually update
If repo's icon doesn't contain latest game resouces, you can check browser console log, checking which item is missing, and maunally download resouce from https://sekai.best/asset_viewer/mysekai/item_preview , and finally add info in html `ITEM_TEXTURES` json

## Special thanks
GPT-4o mini - 写了 90% 的 html 代码。  
DeepSeek R1 - 写了 7% 的 html 代码然后卡炸了。  
