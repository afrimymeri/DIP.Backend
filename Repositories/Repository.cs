using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DIP.Backend.Data;
using DIP.Backend.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DIP.Backend.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly ApplicationDbContext _db;
    protected readonly DbSet<T> _set;

    public Repository(ApplicationDbContext db)
    {
        _db = db;
        _set = _db.Set<T>();
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        // Use AsNoTracking for query performance in read-only scenarios
        return await _set.AsNoTracking().ToListAsync();
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        return await _set.FindAsync(id);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        await _set.AddAsync(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public virtual async Task UpdateAsync(T entity)
    {
        _set.Update(entity);
        await _db.SaveChangesAsync();
    }

    public virtual async Task DeleteAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity is not null)
        {
            _set.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }
}