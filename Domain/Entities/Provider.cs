using System.Collections.Generic;

namespace Domain.Entities
{
    public class Provider
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public ICollection<OAuthAccount> OAuthAccounts { get; set; } = new List<OAuthAccount>();
    }
}
