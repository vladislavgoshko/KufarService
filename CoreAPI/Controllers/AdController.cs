using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace CoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdController : ControllerBase
    {
        // GET: api/<AdController>
        [HttpGet]
        public async Task<IEnumerable<string>> Get()
        {
            using HttpClient httpClient = new HttpClient();
            HttpResponseMessage responseMessage = await httpClient.GetAsync("https://localhost:7275/api/SearchAd?size=5&filters=cat=16010");
            if (responseMessage.IsSuccessStatusCode)
            {
                string res = await responseMessage.Content.ReadAsStringAsync();
                Console.WriteLine(res);
            }
            return null;
        }

        // GET api/<AdController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<AdController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<AdController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<AdController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
