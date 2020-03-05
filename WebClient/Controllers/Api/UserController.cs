using System;
using Serilog;
using System.Text;
using Mapper.Config;
using Mapper.Profile;
using DataAccess.Entity;
using Asset.Common.Services;
using System.Security.Claims;
using Asset.Common.ViewModel;
using System.Threading.Tasks;
using Asset.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Asset.Common.Enumerations;
using System.Collections.Generic;
using WebClient.Controllers.Api.Base;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using BusinessAccess.Service.Interface;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace WebClient.Controllers.Api
{
    [Authorize]
    public class UserController : BaseController
    {
        private readonly IConfiguration _config;
        private readonly IUserService _userService;
        private readonly IMapperFactory _factory;

        public UserController(IConfiguration config, IUserService userService, IMapperFactory factory)
        {
            _config = config;
            _userService = userService;
            _factory = factory;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("Web-Login")]
        public async Task<ActionResult> WebLogin(LoginModel request)
        {
            UserInfoModel result = await _userService.WebLoginAsync(request);
            return Json(await GenerateJSONWebToken(result));
        }
        
        [HttpGet]
        [AllowAnonymous]
        [Route("Get-Users")]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            var users = await _userService.ToListAsync();
            return Json(users);
        }
        
        [HttpGet]
        [AllowAnonymous]
        [Route("Get-By-Id")]
        public async Task<ActionResult<User>> GetUserEntity(Guid id)
        {
            var userEntity = await _userService.FindAsync(id);

            if (userEntity == null)
            {
                return NotFound();
            }

            return Json(userEntity);
        }
        
        [HttpPut]
        [Route("Modified")]
        public async Task<IActionResult> Modified(string idOrCode, ModifiedInfoUserModel user)
        {
            if (string.IsNullOrEmpty(idOrCode) || string.IsNullOrWhiteSpace(idOrCode))
                return BadRequest();

            try
            {
                var userEntity = user.Mapping<User, ModifiedInfoUserModel, UserProfile>(_factory);
                userEntity.Code = idOrCode;
                userEntity.Password = EncryptService.Encrypt(user.Password);

                var data = await _userService.UpdateAsync(userEntity);
                return Json(data);
            }
            catch (ExceptionError e)
            {
                if (e.Error.ErrorCode.Equals((int)ErrorCodeEnum.FailData))
                {
                    Log.Error(e.Error.Data);
                    throw;
                }
                else
                {
                    return NotFound();
                }
            }
        }
        
        [HttpPost]
        [Route("Insert")]
        public async Task<ActionResult<User>> Insert(UserModel user)
        {
            var userEntity = user.Mapping<User, UserModel, UserProfile>(_factory);
            userEntity.Password = EncryptService.Encrypt("Password");

            var data = await _userService.AddAsync(userEntity);
            return Json(data);
        }
        
        [HttpDelete]
        [Route("Delete")]
        public async Task<ActionResult<User>> Delete(Guid id)
        {
            var userEntity = await _userService.FindAsync(id);
            if (userEntity == null)
            {
                return NotFound();
            }

            await _userService.DeleteAsync(userEntity);

            return Json(userEntity);
        }

        private async Task<string> GenerateJSONWebToken(UserInfoModel info)
        {
            if (info == null)
            {
                return string.Empty;
            }
            SymmetricSecurityKey securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            SigningCredentials credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            string fullName = string.IsNullOrEmpty(info.Fullname) ? info.Username : info.Fullname;

            string tokenGuid = Guid.NewGuid().ToString();
            DateTime expried = DateTime.Now.AddMinutes(Math.Max(Convert.ToDouble(_config["Config:TokenExpiryTimeInMinutes"]), 5));
            Claim[] claims = new[] {
                new Claim(JwtRegisteredClaimNames.Sub,  info.Username),
                new Claim("Username", info.Username),
                new Claim("Fullname", fullName),
                new Claim("UserType", info.UserType),
                new Claim("ExpiredPassword", info.ExpiredPassword),
                new Claim("UserId", info.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, tokenGuid)
            };

            JwtSecurityToken token = new JwtSecurityToken(_config["Jwt:Issuer"], _config["Jwt:Issuer"], claims, signingCredentials: credentials);
            string tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            await _userService.UpdateTokenAsync(info.Id, tokenGuid, tokenString, expried);

            return tokenString;
        }
    }
}
