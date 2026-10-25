import requests
import logging
import json
import os

logging.basicConfig(filename='logs/api_data.log', level=logging.ERROR, 
                    format='%(asctime)s - %(levelname)s - %(message)s')

class ParameterRepository:
    def __init__(self, api_url: str, file_path: str):
        self.api_url = api_url
        self.file_path = file_path

    def get_parameters_by_parent_category_id(self, category_id: int):
        try:
            response = requests.get(self.api_url)
            response.raise_for_status()

            data = response.json()
            logging.info("Data successfully fetched from the API.")

            return self.__parse_parameters(data)
        
        except requests.exceptions.RequestException as e:
            logging.error(f"API request error: {e}")


    # def get_categories(self):
    #     """Fetch paramer from API or file"""
    #     try:
    #         response = requests.get(self.api_url)
    #         response.raise_for_status()

    #         data = response.json()
    #         logging.info("Data successfully fetched from the API.")

    #         return self.__parse_categories(data)

    #     except requests.exceptions.RequestException as e:
    #         logging.error(f"API request error: {e}")
    #         print(f"API request error: {e}. Attempting to load data from a file.")

    #         try:
    #             if os.path.exists(self.file_path):
    #                 with open(self.file_path, 'r', encoding='utf-8') as file:
    #                     data = json.load(file)
    #                     logging.info(f"Data successfully loaded from file: {self.file_path}")
    #                     return self.__parse_categories(data)
    #             else:
    #                 logging.error(f"File not found at: {self.file_path}")
    #                 raise FileNotFoundError(f"File not found at: {self.file_path}")

    #         except (FileNotFoundError, json.JSONDecodeError) as file_error:
    #             logging.error(f"Error reading file: {file_error}")
    #             raise file_error

    def __parse_parameters(self, data):
        """Parse the parameters data into objects"""
        pass

        return
