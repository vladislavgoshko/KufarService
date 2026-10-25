import aiohttp
import bs4
import json
import datetime
import asyncio
import logging
from aiogram import Bot, types
from aiogram.enums import ParseMode
from aiogram.client.bot import DefaultBotProperties
from dotenv import load_dotenv
import os
import re

# Загрузка конфигураций из файла .env
load_dotenv()

# Получение токена и chat_id из переменных окружения
TOKEN = os.getenv("TOKEN")
CHAT_ID = os.getenv("CHAT_ID")

# Настройка логирования
logging.basicConfig(
    filename="bot.log",  # Файл для записи логов
    level=logging.INFO,  # Уровень логирования
    format="%(asctime)s - %(levelname)s - %(message)s",  # Формат логов
)

bot = Bot(token=TOKEN, default=DefaultBotProperties(parse_mode=ParseMode.HTML))

headers = {
    "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, как Gecko) Chrome/114.0.0.0 Safari/537.36",
    "accept": "*/*",
}


async def get_goods(session, url="https://www.kufar.by/l"):
    async with session.get(url, headers=headers) as response:
        if response.status != 200:
            return
        kufarPage = bs4.BeautifulSoup(await response.text(), "html.parser")
        script = kufarPage.find_all("script", {"id": "__NEXT_DATA__"})
        jsonKufar = json.loads(script[0].text)
        goodsJSON = jsonKufar["props"]["initialState"]["listing"]["ads"]
        goods = []
        for g in goodsJSON:
            goods.append(
                [
                    g["subject"],
                    int(g["price_byn"]) / 100,
                    datetime.datetime.strptime(g["list_time"], "%Y-%m-%dT%H:%M:%SZ")
                    + datetime.timedelta(hours=3),
                    g["ad_link"],
                ]
            )
        return goods


async def get_info_good(session, url):
    async with session.get(url, headers=headers) as response:
        kufarPage = bs4.BeautifulSoup(await response.text(), "html.parser")
        script = kufarPage.find_all("script", {"id": "__NEXT_DATA__"})
        jsonKufar = json.loads(script[0].text)

        return jsonKufar


def remove_html_tags(text):
    clean_text = re.sub(r"<[^>]+>", "", text)
    return clean_text


def parse_good(json_kufar, good, length_message=-1):
    message_max_length = 1024

    gallery_urls = json_kufar["props"]["initialState"]["adView"]["data"]["images"][
        "gallery"
    ]
    description = json_kufar["props"]["initialState"]["adView"]["data"]["description"]
    seller_name = json_kufar["props"]["initialState"]["adView"]["data"]["userName"]
    seller_url = (
        "https://www.kufar.by/customer/"
        + json_kufar["props"]["initialState"]["adView"]["data"]["accountId"]
    )
    ad_params = json_kufar["props"]["initialState"]["adView"]["data"]["adParams"]
    params = ""
    for param in list(ad_params):
        params += f"<b>{ad_params[param]['pl']}</b>: {str(ad_params[param]['vl']).replace('<', 'меньше ').replace('>', 'больше ')}\n"
    if length_message > message_max_length:
        offset = length_message - message_max_length + 1 # one for symbol '…'
        description = description[:-offset].rstrip()
        description = description + "…"

    message_text = f"""
<b><a href="{good[3]}">{good[0]}</a></b>
Цена: <u>{good[1]} BYN</u>

<blockquote expandable><b>Параметры:</b>
{params}</blockquote>
<blockquote expandable><b>Описание</b>:
{description.replace('<', 'меньше ').replace('>', 'больше ')}</blockquote>

<b>Продавец</b>: <a href="{seller_url}">{seller_name.replace('<', 'меньше ').replace('>', 'больше ')}</a>
{good[2].strftime("%d.%m.%Y, %H:%M:%S")}"""
    length_message = len(remove_html_tags(message_text))
    if length_message > message_max_length:
        return parse_good(json_kufar, good, length_message)
    return gallery_urls, message_text


async def send_media_group_or_send_message(bot, chat_id, text, gallery_urls):

    if not gallery_urls:
        await send_message(bot, chat_id, text)
    else:
        photos = [types.InputMediaPhoto(media=photo) for photo in gallery_urls[0:9]]
        photos[0].caption = text
        await bot.send_media_group(chat_id=chat_id, media=photos)


async def send_message(bot, chat_id, text):
    await bot.send_message(
        chat_id,
        text,
    )


async def main():
    async with aiohttp.ClientSession() as session:
        last_time = datetime.datetime.now() - datetime.timedelta(hours=10)
        last_id = 0
        await send_message(bot, CHAT_ID, "Started")
        while True:
            try:
                goods = await get_goods(
                    session,
                    # "https://re.kufar.by/l/",
                    "https://www.kufar.by/l",
                    # "https://www.kufar.by/l/kompyuternaya-tehnika?elementType=popular_categories",
                )
                if not goods:
                    continue
                for good in goods:
                    if good[2] <= last_time or good[3] == last_id:
                        break

                    print(good[3], good[2])
                    json_kufar = await get_info_good(session, good[3])
                    imgs, text = parse_good(json_kufar, good)
                    await send_media_group_or_send_message(bot, CHAT_ID, text, imgs)
                    last_id = good[3]

                last_time = goods[0][2]
            except Exception as e:
                logging.error(f"Error occurred at {datetime.datetime.now()}:\n{e}\n")
            await asyncio.sleep(60)


if __name__ == "__main__":
    asyncio.run(main())
