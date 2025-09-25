namespace CondotifyAPI.Models.Users
{
    public class UserAccess
    {
        public UserAccess() { }

        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
        public string CPF { get; set; }
        public string RG { get; set; }
        public string BirthDate { get; set; }

        public AccessTypeEnum AccessType { get; set; }
        public bool FirstAccess { get; set; }

        public DateTime LastAccess { get; set; }
        public DateTime CreatedAt { get; set; }

        private UserAccess(string name, string email, string password, string phoneNumber, string cpf, string rg, string birthDate, AccessTypeEnum accessType, bool firstAccess, DateTime lastAccess, DateTime createdAt)
        {
            Id = Guid.NewGuid();
            SetName(name);
            SetEmail(email);
            SetPassword(password);
            SetPhoneNumber(phoneNumber);
            SetCPF(cpf);
            SetRG(rg);
            SetBirthDate(birthDate);
            SetAccessType(accessType);
            SetFirstAccess(firstAccess);
            SetLastAccess(lastAccess);
            SetCreatedAt(createdAt);
        }

        public static UserAccess Create(string name, string email, string password, string phoneNumber, string cpf, string rg, string birthDate, AccessTypeEnum accessType, bool firstAccess, DateTime lastAccess, DateTime createdAt)
        {
            return new UserAccess(name, email, password, phoneNumber, cpf, rg, birthDate, accessType, firstAccess, lastAccess, createdAt);
        }

        public bool Update(string name, string email, string password, string phoneNumber, string cpf, string rg, string birthDate, AccessTypeEnum accessType, bool firstAccess, DateTime lastAccess)
        {
            SetName(name);
            SetEmail(email);
            SetPassword(password);
            SetPhoneNumber(phoneNumber);
            SetCPF(cpf);
            SetRG(rg);
            SetBirthDate(birthDate);
            SetAccessType(accessType);
            SetFirstAccess(firstAccess);
            SetLastAccess(lastAccess);

            return true;
        }

        public void SetCreatedAt(DateTime createdAt)
        {
            CreatedAt = createdAt;
        }

        public void SetLastAccess(DateTime lastAccess)
        {
            LastAccess = lastAccess;
        }

        public void SetFirstAccess(bool firstAccess)
        {
            FirstAccess = firstAccess;
        }

        public void SetAccessType(AccessTypeEnum accessType)
        {
            AccessType = accessType;
        }

        public void SetBirthDate(string birthDate)
        {
            BirthDate = birthDate;
        }

        public void SetRG(string rg)
        {
            RG = rg;
        }

        public void SetCPF(string cpf)
        {
            CPF = cpf;
        }

        public void SetPhoneNumber(string phoneNumber)
        {
            PhoneNumber = phoneNumber;
        }

        public void SetPassword(string password)
        {
            Password = password;
        }

        public void SetEmail(string email)
        {
            Email = email;
        }

        public void SetName(string name)
        {
            Name = name;
        }
    }
}
