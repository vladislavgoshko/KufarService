from Repositories.category_repository import CategoryRepository
import json

class APIDataMeta(type):
    _instances = {}

    def __call__(cls, *args, **kwargs):
        if cls not in cls._instances:
            instance = super().__call__(*args, **kwargs)
            cls._instances[cls] = instance
        return cls._instances[cls]


class APIData(metaclass=APIDataMeta):
    def __init__(self, repository: CategoryRepository) -> None:
        self.__category_repository = repository
        self.__categories = self.__category_repository.get_categories()


    def get_categories(self):
        return self.__categories.copy()


def load_config(config_file):
    """Load configuration from a JSON file"""
    with open(config_file, 'r', encoding='utf-8') as file:
        return json.load(file)


if __name__ == "__main__":
    config = load_config('config.json')
    
    repo = CategoryRepository(
        api_url=config['api_url_category_tree'],
        file_path=config['file_path_category_tree']
    )
    
    api_data = APIData(repository=repo)
    
    for cat in api_data.get_categories():
        print(cat)

