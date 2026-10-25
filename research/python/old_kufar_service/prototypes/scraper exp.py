headers = {
    "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, как Gecko) Chrome/114.0.0.0 Safari/537.36",
    "accept": "*/*",
}

import bs4
import json
import datetime
import aiohttp
import asyncio

class Good:
    def __init__(self, name, price, datetime, url):
        self.Name = name
        self.Price = price
        self.DateTime = datetime
        self.URL = url

    def __str__(self) -> str:
        return f"Name:'{self.Name}' Price:'{self.Price}' DateTime:'{self.DateTime}' URL:'{self.URL}'"
    
TIMEZONE_OFFSET = 3 # Belarus

async def get_json_goods(session, url):
    async with session.get(url, headers=headers) as response:
        if response.status != 200:
            return
        kufar_page = bs4.BeautifulSoup(await response.text(), "html.parser")
        with open("files/page", "w", encoding="utf8") as file:
            file.write(str(kufar_page))
        script = kufar_page.find_all("script", {"id": "__NEXT_DATA__"})
        json_kufar = json.loads(script[0].text)
        json_goods = json_kufar["props"]["initialState"]["listing"]["ads"]

        return json_goods

def json_to_goods(json_goods) -> list:
    goods = []
    for g in json_goods:
        goods.append(
            [
                g["subject"],
                int(g["price_byn"]) / 100,
                datetime.datetime.strptime(g["list_time"], "%Y-%m-%dT%H:%M:%SZ")
                + datetime.timedelta(hours=TIMEZONE_OFFSET),
                g["ad_link"],
            ]
        )
    return goods

async def get_json_good(session, url):
    async with session.get(url, headers=headers) as response:
        kufar_page = bs4.BeautifulSoup(await response.text(), "html.parser")
        script = kufar_page.find_all("script", {"id": "__NEXT_DATA__"})
        json_good = json.loads(script[0].text)
        return json_good

async def main():
    async with aiohttp.ClientSession() as session:
        url = 'https://www.kufar.by/l'
        json_goods = await get_json_goods(session, url)
        list_goods = json_to_goods(json_goods)

        goods = []
        for good in list_goods:
            goods.append(Good(good[0], good[1], good[2], good[3]))

        good = await get_json_good(session, goods[0].URL)

        with open("files/goods", "w", encoding="utf8") as file:
            for g in goods:
                file.write(str(g) + '\n')

if __name__ == "__main__":
    asyncio.run(main())