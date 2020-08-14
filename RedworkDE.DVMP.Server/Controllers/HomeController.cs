using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RedworkDE.DVMP.Server.Data;
using RedworkDE.DVMP.Server.Models;

namespace RedworkDE.DVMP.Server.Controllers
{
	public class HomeController : Controller
	{
		private readonly ILogger<HomeController> _logger;

		public HomeController(ILogger<HomeController> logger)
		{
			_logger = logger;
		}

		public IActionResult Index()
		{
			return View();
		}

		public IActionResult Privacy()
		{
			return View();
		}

		[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
		public IActionResult Error()
		{
			return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
		}

		public IActionResult SessionList()
		{
			return View(DataContainer.Sessions.Values);
		}


		public IActionResult SessionDetails(Guid id)
		{
			if (!DataContainer.Sessions.TryGetValue(id, out var session)) return Error();
			return View(session);
		}

		public IActionResult UserList()
		{
			return View(DataContainer.Users.Values);
		}

		public IActionResult UserDetails(Guid id)
		{
			if (!DataContainer.Users.TryGetValue(id, out var user)) return Error();
			return View(user);
		}
	}
}
