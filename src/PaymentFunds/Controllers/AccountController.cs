using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PaymentFunds.Models;
using Stripe;

namespace PaymentFunds.Controllers;

public class AccountController : Controller
{
  private readonly SignInManager<ApplicationUser> _signInManager;
  private readonly ILogger<AccountController> _logger;

  public AccountController(
    SignInManager<ApplicationUser> signInManager,
    ILogger<AccountController> logger)
  {
    _signInManager = signInManager;
    _logger = logger;
  }

  [HttpGet]
  [AllowAnonymous]
  public IActionResult Login(string? returnUrl = null)
  {
    return View(new LoginViewModel { ReturnUrl = returnUrl });
  }

  [HttpPost]
  [AllowAnonymous]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> Login(LoginViewModel model)
  {
    if (!ModelState.IsValid)
    {
      return View(model);
    }

    var result = await _signInManager.PasswordSignInAsync(
      model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

    if (result.Succeeded)
    {
      _logger.LogInformation("User {Email} signed in .", model.Email);
      return RedirectToLocal(model.ReturnUrl);
    }

    if (result.IsLockedOut)
    {
      _logger.LogWarning("Locked out account attempted sign in: {Email}", model.Email);
      ModelState.AddModelError(string.Empty, "This account is locked. Try again later.");
      return View(model);
    }

    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
    return View(model);
  }

  [HttpPost]
  [Authorize]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> Logout()
  {
    await _signInManager.SignOutAsync();
    return RedirectToAction(nameof(Login));
  }

  [HttpGet]
  [AllowAnonymous]
  public IActionResult AccessDenied() => View();
  public IActionResult RedirectToLocal(string? returnUrl) =>
    Url.IsLocalUrl(returnUrl)
    ? Redirect(returnUrl)
    : RedirectToAction("Index", "PaymentRequests");
}