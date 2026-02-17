using Microsoft.AspNetCore.Identity;

namespace CondotifyAPI.Domain.Models.Users
{
    public class UserAccess
    {
        public UserAccess() { }

        public Guid Id { get; set; }
        public string Name { get; private set; }
        public string Email { get; private set; }
        public string PasswordHash { get; private set; }
        public string PhoneNumber { get; private set; }
        public string CPF { get; private set; }
        public string RG { get; private set; }
        public string BirthDate { get; private set; }

        public AccessTypeEnum AccessType { get; private set; }
        public bool FirstAccess { get; private set; }

        public DateTime LastAccess { get; private set; }
        public DateTime CreatedAt { get; private set; }

        public Guid EnterpriseId { get; private set; }

        private UserAccess(
            string name,
            string email,
            string passwordHash,
            string phoneNumber,
            string cpf,
            string rg,
            string birthDate,
            AccessTypeEnum accessType,
            bool firstAccess,
            DateTime lastAccess,
            DateTime createdAt,
            Guid enterpriseId)
        {
            Id = Guid.NewGuid();
            Name = name;
            Email = email;
            PasswordHash = passwordHash;
            PhoneNumber = phoneNumber;
            CPF = cpf;
            RG = rg;
            BirthDate = birthDate;
            AccessType = accessType;
            FirstAccess = firstAccess;
            LastAccess = lastAccess;
            CreatedAt = createdAt;
            EnterpriseId = enterpriseId;
        }

        public static UserAccess Create(
            string name,
            string email,
            string password,
            string phoneNumber,
            string cpf,
            string rg,
            string birthDate,
            AccessTypeEnum accessType,
            bool firstAccess,
            DateTime lastAccess,
            DateTime createdAt,
            IPasswordHasher<UserAccess> hasher,
            Guid? enterpriseId = null)
        {
            var passwordHash = hasher.HashPassword(null, password);
            return new UserAccess(
                name,
                email,
                passwordHash,
                phoneNumber,
                cpf,
                rg,
                birthDate,
                accessType,
                firstAccess,
                lastAccess,
                createdAt,
                enterpriseId ?? Guid.NewGuid());
        }

        public bool Update(
            string name,
            string email,
            string password,
            string phoneNumber,
            string cpf,
            string rg,
            string birthDate,
            AccessTypeEnum accessType,
            bool firstAccess,
            DateTime lastAccess,
            IPasswordHasher<UserAccess> hasher)
        {
            SetName(name);
            SetEmail(email);
            if (!string.IsNullOrWhiteSpace(password))
                SetPassword(password, hasher);
            SetPhoneNumber(phoneNumber);
            SetCPF(cpf);
            SetRG(rg);
            SetBirthDate(birthDate);
            SetAccessType(accessType);
            SetFirstAccess(firstAccess);
            SetLastAccess(lastAccess);
            return true;
        }

        public void SetPassword(string password, IPasswordHasher<UserAccess> hasher)
        {
            PasswordHash = hasher.HashPassword(this, password);
        }

        public void SetEmail(string email) => Email = email;
        public void SetName(string name) => Name = name;
        public void SetPhoneNumber(string phoneNumber) => PhoneNumber = phoneNumber;
        public void SetCPF(string cpf) => CPF = cpf;
        public void SetRG(string rg) => RG = rg;
        public void SetBirthDate(string birthDate) => BirthDate = birthDate;
        public void SetAccessType(AccessTypeEnum accessType) => AccessType = accessType;
        public void SetFirstAccess(bool firstAccess) => FirstAccess = firstAccess;
        public void SetLastAccess(DateTime lastAccess) => LastAccess = lastAccess;
        public void SetCreatedAt(DateTime createdAt) => CreatedAt = createdAt;

        public bool VerifyPassword(string password, IPasswordHasher<UserAccess> hasher)
        {
            var result = hasher.VerifyHashedPassword(this, PasswordHash, password);
            return result != PasswordVerificationResult.Failed;
        }
    }
}
