from mysql.connector import pooling
from mysql.connector.connection import MySQLConnection
from contextlib import contextmanager
from typing import Generator

class MySQLDatabase:
    """
    Класс для управления подключениями к базе данных MySQL.
    Использует пул соединений для повышения производительности.
    """

    def __init__(self, host: str, user: str, password: str, database: str, pool_size: int = 5):
        self.config = {
            "host": host,
            "user": user,
            "password": password,
            "database": database,
            
        }
        self.pool = pooling.MySQLConnectionPool(pool_name="mypool", pool_size=pool_size, **self.config)

    @contextmanager
    def get_connection(self) -> Generator[MySQLConnection, None, None]:
        """
        Контекстный менеджер для получения соединения из пула.
        """
        connection = self.pool.get_connection()
        try:
            yield connection
        finally:
            connection.close()

    @contextmanager
    def get_cursor(self):
        """
        Контекстный менеджер для получения курсора соединения.
        """
        with self.get_connection() as connection:
            cursor = connection.cursor(dictionary=True)
            try:
                yield cursor
            finally:
                cursor.close()