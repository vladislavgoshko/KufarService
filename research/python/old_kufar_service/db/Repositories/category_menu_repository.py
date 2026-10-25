from typing import List, Optional
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from sql_database import MySQLDatabase  # Импортируем класс MySQLDatabase

class CategoryRepository:
    def __init__(self, db: MySQLDatabase):
        self.db = db

    def get_all_categories(self) -> List[dict]:
        """
        Получить все категории.
        """
        query = "SELECT * FROM CategoryMenuText"
        with self.db.get_cursor() as cursor:
            cursor.execute(query)
            return cursor.fetchall()

    def get_category_by_id(self, category_id: int) -> Optional[dict]:
        """
        Получить категорию по её ID.
        """
        query = "SELECT * FROM CategoryMenuText WHERE CategoryID = %s"
        with self.db.get_cursor() as cursor:
            cursor.execute(query, (category_id,))
            return cursor.fetchone()

    def add_category(self, category_id: int, row: int, column: int, text_ru: Optional[str]) -> None:
        """
        Добавить новую категорию.
        """
        query = """
        INSERT INTO CategoryMenuText (CategoryID, `Row`, `Column`, TextRu)
        VALUES (%s, %s, %s, %s)
        """
        with self.db.get_cursor() as cursor:
            cursor.execute(query, (category_id, row, column, text_ru))
            cursor.connection.commit()

    def update_category_text(self, category_id: int, text_ru: str) -> None:
        """
        Обновить текст категории.
        """
        query = """
        UPDATE CategoryMenuText
        SET TextRu = %s
        WHERE CategoryID = %s
        """
        with self.db.get_cursor() as cursor:
            cursor.execute(query, (text_ru, category_id))
            cursor.connection.commit()

    def delete_category(self, category_id: int) -> None:
        """
        Удалить категорию по её ID.
        """
        query = "DELETE FROM CategoryMenuText WHERE CategoryID = %s"
        with self.db.get_cursor() as cursor:
            cursor.execute(query, (category_id,))
            cursor.connection.commit()
