using AI.DocumentAssistant.Application.Abstractions.Authentication;
using AI.DocumentAssistant.Application.Auth.Dtos;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Auth.Services
{
    public sealed class AuthService
    {
        private readonly AppDbContext _dbContext;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenService _jwtTokenService;

        public AuthService(
            AppDbContext dbContext,
            IPasswordHasher passwordHasher,
            IJwtTokenService jwtTokenService)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
            _jwtTokenService = jwtTokenService;
        }

        public async Task RegisterAsync(RegisterUserDto dto, CancellationToken cancellationToken)
        {
            var exists = await _dbContext.Users.AnyAsync(x => x.Email == dto.Email, cancellationToken);
            if (exists)
            {
                throw new BadRequestException("User with this email already exists.");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = dto.Email.ToLowerInvariant(),
                PasswordHash = _passwordHasher.Hash(dto.Password),
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<AuthResultDto> LoginAsync(LoginUserDto dto, CancellationToken cancellationToken)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(x => x.Email == dto.Email.ToLower(), cancellationToken);

            if (user is null || !_passwordHasher.Verify(dto.Password, user.PasswordHash))
            {
                throw new UnauthorizedException("Invalid credentials.");
            }

            var accessToken = _jwtTokenService.GenerateAccessToken(user);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();
            refreshToken.UserId = user.Id;

            _dbContext.RefreshTokens.Add(refreshToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new AuthResultDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                ExpiresIn = 3600
            };
        }

        public async Task<AuthResultDto> RefreshAsync(RefreshTokenDto dto, CancellationToken cancellationToken)
        {
            var token = await _dbContext.RefreshTokens
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Token == dto.RefreshToken, cancellationToken);

            if (token is null || token.IsRevoked || token.ExpiresAtUtc <= DateTime.UtcNow)
            {
                throw new UnauthorizedException("Invalid refresh token.");
            }

            token.IsRevoked = true;

            var newRefreshToken = _jwtTokenService.GenerateRefreshToken();
            newRefreshToken.UserId = token.UserId;

            _dbContext.RefreshTokens.Add(newRefreshToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new AuthResultDto
            {
                AccessToken = _jwtTokenService.GenerateAccessToken(token.User),
                RefreshToken = newRefreshToken.Token,
                ExpiresIn = 3600
            };
        }
    }
}
