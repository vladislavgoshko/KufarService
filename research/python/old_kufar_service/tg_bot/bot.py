from aiogram import F
from aiogram.types import CallbackQuery
from aiogram.filters import Command, callback_data
from aiogram.types import InlineKeyboardButton, InlineKeyboardMarkup
from aiogram import Bot, Dispatcher, types
import asyncio
from dotenv import load_dotenv
import os
import sys
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from Repositories.category_repository import *
from db import service
from tools import CategorieCallback
from tools import create_subcategory_inlineKeyboardMurkup, create_category_buttons

load_dotenv()

API_TOKEN = os.getenv('API_TOKEN')

if not API_TOKEN:
    raise ValueError("Переменная окружения API_TOKEN не установлена")

bot = Bot(token=API_TOKEN)
dp = Dispatcher()

cp = CategoryRepository(r'https://api.kufar.by/category-tree/v1/category_tree',
                            r'C:\Users\goshko\Desktop\kufar_ad\api_data\category_tree.json')

cats = cp.get_categories()

categories = create_category_buttons(service.get_category_rows())

@dp.callback_query(CategorieCallback.filter(F.foo == "demo"))
async def my_callback_foo(query: CallbackQuery, callback_data: CategorieCallback):
    if callback_data.categorie == 0:
        await query.message.edit_text("Добро пожаловать в наш магазин! Выберите категорию:", reply_markup=categories)
    else:
        category = next(
        (obj for obj in cats if obj.id == callback_data.categorie), None)
        await query.message.edit_text(f"{category.name}:", reply_markup=create_subcategory_inlineKeyboardMurkup(category))

@dp.message(Command('start'))
async def send_welcome(message: types.Message):
    await message.answer("Добро пожаловать в наш магазин! Выберите категорию:", reply_markup=categories)


async def main():
    await dp.start_polling(bot)

if __name__ == '__main__':
    asyncio.run(main())
