using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SampleNamespace
{
    /// <summary>A sample person class for testing.</summary>
    public class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }

        public Person(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Greet() => $"Hello, {Name}!";

        public static Person FromString(string input)
        {
            var parts = input.Split(',');
            return new Person(parts[0].Trim(), int.Parse(parts[1].Trim()));
        }
    }

    public interface IRepository<T>
    {
        Task<T?> GetByIdAsync(int id);
        Task<IEnumerable<T>> GetAllAsync();
    }

    public sealed class PersonRepository : IRepository<Person>
    {
        private readonly Dictionary<int, Person> _store = new();

        public Task<Person?> GetByIdAsync(int id)
        {
            _store.TryGetValue(id, out var p);
            return Task.FromResult(p);
        }

        public Task<IEnumerable<Person>> GetAllAsync() =>
            Task.FromResult<IEnumerable<Person>>(_store.Values);

        public void Add(int id, Person person) => _store[id] = person;
    }

    public enum Status { Active, Inactive, Archived }

    public record struct Point(double X, double Y);
}
