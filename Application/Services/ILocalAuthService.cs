using System.Threading.Tasks;
using Domain.Entities;
using Domain.Request;

namespace Application.Services
{
    public interface ILocalAuthService
    {
        Task<User> SignupAsync(SignupRequest request);
        Task<bool> VerifyPasswordAsync(string password, Domain.Entities.LocalAccount localAccount);
    }
}
