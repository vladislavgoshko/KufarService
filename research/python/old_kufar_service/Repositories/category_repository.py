import requests
import logging
import json
import os
import sys
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from categories import Categorie, Subcategorie

logging.basicConfig(filename='logs/api_data.log', level=logging.ERROR, 
                    format='%(asctime)s - %(levelname)s - %(message)s')

class CategoryRepository:
    def __init__(self, api_url: str, file_path: str):
        self.api_url = api_url
        self.file_path = file_path

    def get_categories(self) -> list[Categorie]:
        """Fetch categories from API or file"""
        try:
            response = requests.get(self.api_url)
            response.raise_for_status()

            data = response.json()
            logging.info("Data successfully fetched from the API.")

            return self.__parse_categories(data)

        except requests.exceptions.RequestException as e:
            logging.error(f"API request error: {e}")
            print(f"API request error: {e}. Attempting to load data from a file.")

            try:
                if os.path.exists(self.file_path):
                    with open(self.file_path, 'r', encoding='utf-8') as file:
                        data = json.load(file)
                        logging.info(f"Data successfully loaded from file: {self.file_path}")
                        return self.__parse_categories(data)
                else:
                    logging.error(f"File not found at: {self.file_path}")
                    raise FileNotFoundError(f"File not found at: {self.file_path}")

            except (FileNotFoundError, json.JSONDecodeError) as file_error:
                logging.error(f"Error reading file: {file_error}")
                raise file_error

    def __parse_categories(self, data) -> list[Categorie]:
        """Parse the categories data into objects"""
        categories = []
        category_list = data.get('categories', [])

        for categ in category_list:
            subcategories = []
            for subcateg in categ['subcategories']:
                subcategories.append(
                    Subcategorie(
                        int(subcateg['id']),
                        subcateg['name'],
                        subcateg['name_ru'],
                        subcateg['name_by'],
                        int(subcateg['order']),
                        int(subcateg['parent']),
                    )
                )

            categories.append(
                Categorie(
                    int(categ['id']),
                    categ['name'],
                    categ['name_ru'],
                    categ['name_by'],
                    int(categ['version']),
                    int(categ['order']),
                    subcategories
                )
            )

        return categories

def group_strings(strings):
    group_3 = []
    group_2 = []
    group_1 = []
    result = []

    # Разделение строк на группы в зависимости от длины
    for string in strings:
        if len(string) < 10:
            group_3.append(string)
        elif len(string) <= 15:
            group_2.append(string)
        else:
            group_1.append(string)
            result.append([string])

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


if __name__=="__main__":
    cp = CategoryRepository('https://api.kufar.by/category-tree/v1/category_tree', 'api_data/category_tree.json')
    cats = cp.get_categories()
    # cats.sort(key = lambda x : x.name)
    for cat in cats:
        print(group_strings([(subcat.name, subcat.id) for subcat in cat.subcategories]))
        # print()
        print(f"{cat.name} {cat.id}")
        # for subcat in cat.subcategories:
        #     print(f'\t{subcat.name}')
        