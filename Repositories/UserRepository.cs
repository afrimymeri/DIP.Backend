using DIP.Backend.Data;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;

namespace DIP.Backend.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext db) : base(db)
    {
    }
}