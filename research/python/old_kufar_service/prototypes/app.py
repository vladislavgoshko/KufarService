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
from pathlib import Path
import argparse

# Загрузка конфигураций из файла .env
load_dotenv()

# Получение токена и chat_id из переменных окружения
TOKEN = os.getenv("API_TOKEN")
CHAT_ID = os.getenv("CHAT_ID")

# Настройка логирования
logging.basicConfig(
    filename="bot.log",
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)

bot = Bot(token=TOKEN, default=DefaultBotProperties(parse_mode=ParseMode.HTML))

headers = {
    "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, как Gecko) Chrome/114.0.0.0 Safari/537.36",
    "accept": "*/*",
}

def save_last_times(last_times: dict):
    data = {url: ts.isoformat() for url, ts in last_times.items()}
    with open("last_time.json", "w") as f:
        json.dump(data, f)

def load_last_times() -> dict:
    try:
        with open("last_time.json", "r") as f:
            data = json.load(f)
            return {url: datetime.datetime.fromisoformat(ts) for url, ts in data.items()}
    except (FileNotFoundError, json.JSONDecodeError, KeyError):
        return {}

async def get_goods(session, url):
    async with session.get(url, headers=headers) as response:
        if response.status != 200:
            return []
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
                    datetime.datetime.strptime(g["list_time"], "%Y-%m-%dT%H:%M:%SZ") + datetime.timedelta(hours=3),
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
    return re.sub(r"<[^>]+>", "", text)

def parse_good(json_kufar, good, length_message=-1):
    message_max_length = 1024
    gallery_urls = json_kufar["props"]["initialState"]["adView"]["data"]["images"]["gallery"]
    description = json_kufar["props"]["initialState"]["adView"]["data"]["description"]
    seller_name = json_kufar["props"]["initialState"]["adView"]["data"]["userName"]
    seller_url = "https://www.kufar.by/customer/" + json_kufar["props"]["initialState"]["adView"]["data"]["accountId"]
    ad_params = json_kufar["props"]["initialState"]["adView"]["data"]["adParams"]

    params = ""
    for param in ad_params:
        val = str(ad_params[param]["vl"]).replace("<", "меньше ").replace(">", "больше ")
        params += f"<b>{ad_params[param]['pl']}</b>: {val}\n"

    if length_message > message_max_length:
        offset = length_message - message_max_length + 1
        description = description[:-offset].rstrip() + "…"

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
        photos = []
        # Telegram поддерживает до 10 файлов в медиагруппе (от 0 до 9 = 10 шт. [0:10])
        for i, photo in enumerate(gallery_urls[:10]):
            if i == 0:
                # Для первого фото сразу передаем подпись (caption)
                photos.append(types.InputMediaPhoto(media=photo, caption=text))
            else:
                # Остальные фото без подписи
                photos.append(types.InputMediaPhoto(media=photo))
                
        await bot.send_media_group(chat_id=chat_id, media=photos)

async def send_message(bot, chat_id, text):
    await bot.send_message(chat_id, text)

async def main(urls: list[str]):
    try:
        async with aiohttp.ClientSession() as session:
            last_times = load_last_times()
            last_ids = {}

            await send_message(bot, CHAT_ID, "Started")

            while True:
                try:
                    for url in urls:
                        goods = await get_goods(session, url)
                        if not goods:
                            continue

                        last_time = last_times.get(url, datetime.datetime.now() - datetime.timedelta(hours=10))
                        last_id = last_ids.get(url, "")

                        for good in goods:
                            if good[2] <= last_time or good[3] == last_id:
                                break

                            print(f"[{url}]\n{good[3]} {good[2]}")
                            json_kufar = await get_info_good(session, good[3])
                            imgs, text = parse_good(json_kufar, good)
                            await send_media_group_or_send_message(bot, CHAT_ID, text, imgs)
                            last_ids[url] = good[3]

                        if goods:
                            last_times[url] = goods[0][2]
                    save_last_times(last_times)

                except asyncio.CancelledError:
                    # Корректно завершаем работу, если скрипт убили
                    break
                except Exception as e:
                    logging.error(f"Error occurred at {datetime.datetime.now()}:\n{e}\n")
                
                await asyncio.sleep(60)
    finally:
        # Корректно закрываем сессию бота перед выходом из программы
        await bot.session.close()

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Kufar parser for multiple URLs")
    parser.add_argument(
        "urls", nargs="+", help="One or more Kufar listing URLs to monitor"
    )
    args = parser.parse_args()

    try:
        asyncio.run(main(args.urls))
    except KeyboardInterrupt:
        print("Бот остановлен пользователем.")