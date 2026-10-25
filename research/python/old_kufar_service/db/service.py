
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from db.sql_database import MySQLDatabase
from db.Repositories.category_menu_repository import CategoryRepository

# Создание подключения к базе данных через MySQLDatabase
db = MySQLDatabase(
    host="localhost",
    user="root",
    password="7145880",
    database="KUFAR",
    pool_size=5
)

def convert_emoji_code_to_emoji(input_string):
    import re

    # Регулярное выражение для поиска паттернов U+XXXX
    pattern = r'U\+([A-Fa-f0-9]{4,6})'
    
    # Функция для замены
    def replace_match(match):
        # Получаем код в шестнадцатеричном формате
        hex_code = match.group(1)
        # Преобразуем в символ
        return chr(int(hex_code, 16))
    
    # Заменяем все вхождения паттерна
    return re.sub(pattern, replace_match, input_string)

def get_category_rows():
    category_repo = CategoryRepository(db)
    all_cats = category_repo.get_all_categories()
    tmp = [(cat['CategoryID'], cat['Row'], cat['Column'], convert_emoji_code_to_emoji(cat['TextRu'])) for cat in all_cats]
    sorted_data = sorted(tmp, key=lambda x: (x[1], x[2]))
    return sorted_data
