
from aiogram.filters import callback_data
from aiogram.types import InlineKeyboardButton, InlineKeyboardMarkup
import os
import sys
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from Repositories.category_repository import *

class CategorieCallback(callback_data.CallbackData, prefix="my"):
    foo: str
    categorie: int


def group_inlineButtons(buttons: list[InlineKeyboardButton]) -> list[list[InlineKeyboardButton]]:
    group_3 = []
    group_2 = []
    group_1 = []
    result = []

    # Разделение строк на группы в зависимости от длины
    for button in buttons:
        if len(button.text) < 10:
            group_3.append(button)
        elif len(button.text) <= 20:
            group_2.append(button)
        else:
            group_1.append(button)
            result.append([button])

    # Максимизация групп по 3, затем по 2
    while len(group_3) >= 3:
        result.append(group_3[:3])
        group_3 = group_3[3:]

    while len(group_2) >= 2:
        result.append(group_2[:2])
        group_2 = group_2[2:]

    # Добавление оставшихся элементов
    if group_3:
        result.append(group_3)
    if group_2:
        result.append(group_2)

    return result


def create_subcategory_inlineKeyboardMurkup(category: Categorie) -> InlineKeyboardMarkup:
    if category == None:
        return
    subcategories = category.subcategories
    inlineKeyboardButtons = [InlineKeyboardButton(text=subcat.name, callback_data=CategorieCallback(
        foo="demo", categorie=0).pack()) for subcat in subcategories]

    grouped_buttons = group_inlineButtons(inlineKeyboardButtons)

    return InlineKeyboardMarkup(inline_keyboard=grouped_buttons)

def create_category_buttons(rows):
    """Создание кнопок для InlineKeyboardMarkup на основе данных из базы данных.
    Сортировка сначала по строкам, затем по колонкам.
    """
    # Сортируем по номеру строки, затем по номеру колонки
    rows.sort(key=lambda x: (x[1], x[2]))

    categories = []
    row = []
    current_row = 1
    
    for r in rows:
        category_id, row_num, column_num, text = r
        if row_num != current_row:  # Если строка изменилась, добавляем текущую строку в список и начинаем новую
            categories.append(row)
            row = []
            current_row = row_num
        row.append(InlineKeyboardButton(text=text, callback_data=CategorieCallback(
            foo="demo", categorie=category_id).pack()))
    
    categories.append(row)  # Добавляем последнюю строку
    return InlineKeyboardMarkup(inline_keyboard=categories)
