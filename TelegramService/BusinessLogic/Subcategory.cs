using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramService.BusinessLogic
{
    internal class Subcategory
    {
        public string Title { get; }
        public int Id { get; }
        public Subcategory(string title, int id)
        {
            Title = title;
            Id = id;
        }

    }
}
