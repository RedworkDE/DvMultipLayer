using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RedworkDE.DVMP.Server.Common;

namespace RedworkDE.DVMP.Server.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class SessionController : ControllerBase
	{
		[HttpPost("user")]
		public CreateUserResponse Create(CreateUserResponse user)
		{
			return new CreateUserResponse();
		}

		//// GET api/<SessionController>/5
		//[HttpGet("{id}")]
		//public string Get(int id)
		//{
		//	return "value";
		//}

		//// POST api/<SessionController>
		//[HttpPost]
		//public void Post([FromBody] string value)
		//{
		//}

		//// PUT api/<SessionController>/5
		//[HttpPut("{id}")]
		//public void Put(int id, [FromBody] string value)
		//{
		//}

		//// DELETE api/<SessionController>/5
		//[HttpDelete("{id}")]
		//public void Delete(int id)
		//{
		//}
	}
}
