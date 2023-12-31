﻿using AutoMapper;
using Esafe_Team_Project.Data.Enums;
using Esafe_Team_Project.Data;
using Esafe_Team_Project.Entities;
using Esafe_Team_Project.Helpers;
using Esafe_Team_Project.Models.Address;
using Esafe_Team_Project.Models.Client.Request;
using Esafe_Team_Project.Models.Client.Response;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Esafe_Team_Project.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;
using System.Reflection;
using SelectPdf;
using System.Drawing;

namespace Esafe_Team_Project.Services
{
    public class ClientService
    {
        private readonly Jwt _jwt;
        private readonly AppDbContext _dbContext;
        private readonly IMapper _mapper;
        private readonly IEmailClient _mailer;
        public ClientService(AppDbContext dbContext, IMapper mapper, IOptions<Jwt> optionsJwt, IEmailClient mailer)
        {
            _dbContext = dbContext;
            _mapper = mapper;
            _jwt = optionsJwt.Value;
            _mailer = mailer;
        }

        public string HashPassword(string password)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public bool VerifyPassword(string password, string passwordHash)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                string hashedPassword = builder.ToString();

                return passwordHash.Equals(hashedPassword, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string generateJwtToken(Client user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwt.Key);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("id", user.Id.ToString()), new Claim("Role", user.Role.ToString()) }),
                Expires = DateTime.UtcNow.AddMinutes(_jwt.ExpireTime),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }


        private string generateJwtTokenAdmin(Admin user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwt.Key);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("id", user.Id.ToString()), new Claim("Role", user.Role.ToString()) }),
                Expires = DateTime.UtcNow.AddMinutes(_jwt.ExpireTime),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private string randomTokenString()
        {
            using var rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            var randomBytes = new byte[40];
            rngCryptoServiceProvider.GetBytes(randomBytes);
            // convert random bytes to hex string
            return BitConverter.ToString(randomBytes).Replace("-", "");
        }














        public async Task<Client> AddClient(ClientDto client)
        {
            Console.WriteLine("eneterd service");
            var newClient = _mapper.Map<Client>(client);
            _dbContext.Clients.Add(newClient);

            Console.WriteLine(newClient.ClientAddresses);

            await _dbContext.SaveChangesAsync();

            return newClient;
        }

        public async Task Register(ClientDto client)
        {
            Console.WriteLine("entered registeration service");
            if (_dbContext.Clients.Any(x => x.NationalId == client.NationalId))
            {
                Console.WriteLine("client with same national id: {0} already exists", client.NationalId);
                throw new Exception("Client with this national already exists");
            }
            else if (_dbContext.Clients.Any(x => x.Username == client.Username))
            {
                Console.WriteLine("client with same username: {0} already exists", client.Username);
            }
            else
            {
                Console.WriteLine("checking pass & conf pass");
                if (client.Password != null && client.ConfirmPassword != null)
                {
                    if (client.Password.Equals(client.ConfirmPassword))
                    {
                        Console.WriteLine("passwords match, checking client role...");

                        var newClient = _mapper.Map<Client>(client);
                        if (newClient.Email.Contains("uaedigitallab"))
                        {
                            newClient.Role = Role.Admin;

                        }
                        else
                        {
                            newClient.Role = Role.Client;
                        }
                        newClient.Password = HashPassword(client.Password);
                        newClient.AccountNo = GetAccountNo();
                        newClient.balance = GenerateBalance();
                        newClient.OTPExpiry = DateTime.Now.AddMinutes(5);
                        newClient.RemainingAttempts = 5;
                        newClient.OTP = (short)new Random().Next(1000, 9999);
                        newClient.Verified = false;
                        try
                        {
                            _dbContext.Clients.Add(newClient);
                            _dbContext.SaveChanges();
                            await _mailer.SendAsync(newClient.Email, "Bank | Verification Code.", "Your verification code is  " + newClient.OTP.ToString());
                            Console.WriteLine("client added successfully");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }

                    }
                    else
                    {
                        Console.WriteLine("Passwords do not match.");
                        throw new Exception("Passwords do not match");
                    }
                }
                else
                {
                    Console.WriteLine("Password and confirm password fields cannot be empty.");
                    throw new Exception("Password and confirm password fields cannot be empty.");
                }

            }
        }

        public static string GetAccountNo()
        {
            return "11111111111";
        }

        //Authentication Service
        public AuthenticateResponse Authenticate(ClientLoginDto client, string ipAddress)
        {
            if (client == null)
            {
                Console.Write("client input is null");
                throw new Exception("error in the input");

            }
            if (_dbContext.Clients.Any(x => x.Username == client.Username))
            {
                var logClient = _dbContext.Clients.FirstOrDefault(entity => entity.Username == client.Username);
                if(logClient?.Verified == false) {
                    throw new Exception("Unverified.");
                }
                if (VerifyPassword(client.Password, logClient.Password))
                {
                    // authentication successful so generate jwt and refresh tokens
                    var jwtToken = generateJwtToken(logClient);


                    // save refresh token
                    _dbContext.Update(logClient);
                    _dbContext.SaveChanges();


                    var response = _mapper.Map<AuthenticateResponse>(logClient);
                    response.JwtToken = jwtToken;
                    return response;

                }
                else
                {
                    throw new Exception("Wrong password.");
                }

            }
            else if (_dbContext.Admins.Any(x => x.Username == client.Username))
            {
                var logClient = _dbContext.Admins.FirstOrDefault(entity => entity.Username == client.Username);
                if (VerifyPassword(client.Password, logClient.Password))
                {
                    // authentication successful so generate jwt and refresh tokens
                    var jwtToken = generateJwtTokenAdmin(logClient);
                    Console.WriteLine(logClient);
                    _dbContext.Update(logClient);
                    _dbContext.SaveChanges();
                    var response = _mapper.Map<AuthenticateResponse>(logClient);
                    response.JwtToken = jwtToken;
                    return response;

                }
                else
                {
                    throw new Exception("Wrong password.");
                }

            }
            else
            {
                Console.Write("Username does not exist.");
                throw new Exception("Username does not exist.");
            }
        }

        public async Task<ClientDisplayDto> GetClientAuth(Client client)
        {
            var clientResp = _mapper.Map<ClientDisplayDto>(client);
            return clientResp;
        }

        public async Task<List<ClientDisplayDto>> GetAll()
        {
            var clients = await _dbContext.Clients.Include(_ => _.ClientAddresses).Include(_ => _.ClientCertificates).ToListAsync();
            Console.WriteLine(clients.ToString());
            List<ClientDisplayDto> clientsDto = _mapper.Map<List<ClientDisplayDto>>(clients);
            Console.WriteLine(clientsDto.ToString());
            return clientsDto;
        }

        public async Task<List<ClientDisplayDto>> GetClientbyID(int id)
        {
            var client = await _dbContext.Clients.Where(client => client.Id == id).Include(_ => _.ClientAddresses).Include(_ => _.ClientCertificates).ToListAsync();
            Console.WriteLine(client.ToString());
            List<ClientDisplayDto> retClient = _mapper.Map<List<ClientDisplayDto>>(client);
            return retClient;
        }

        public async Task<ClientDisplayDto> AddAddress(int id, AddressDto address)
        {
            AddressDto newaddress = new AddressDto(address.Country, address.City, address.Street);
            var client = await _dbContext.Clients.FindAsync(id);


            var finAdd = _mapper.Map<Address>(newaddress);
            client.ClientAddresses.Add(finAdd);
            finAdd.Client = client;
            finAdd.ClientID = id;
            _dbContext.Addresses.Add(finAdd);
            _dbContext.Entry(client).State = EntityState.Modified;

            try
            {
                await _dbContext.SaveChangesAsync();
                var retClient = _mapper.Map<ClientDisplayDto>(client);
                Console.WriteLine(client.ToString());
                return retClient;
            }
            catch
            {
                return null;

            }

        }

        public async Task UpdatePassword(string password, Client client)
        {
            string pass = HashPassword(password);
            client.Password = pass;
            _dbContext.Entry(client).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<TransferResponse>> GetTransferInfo(Client client)
        {
            List<Transfer> transfers = await _dbContext.Transfers.Where(_ => _.SenderId == client.Id||_.RecieverId==client.Id).ToListAsync();
            if (transfers != null)
            {
                List<TransferResponse> transferdto = _mapper.Map<List<TransferResponse>>(transfers);
                return transferdto;
            }
            else
            {
                return null;
            }
        }

        public async Task<List<TransferResponse>> GetTransferInfoById(int id)
        {
            List<Transfer> transfers = await _dbContext.Transfers.Where(_ => _.RecieverId == id).ToListAsync();
            if (transfers != null)
            {
                List<TransferResponse> transferdto = _mapper.Map<List<TransferResponse>>(transfers);
                return transferdto;
            }
            else
            {
                return null;
            }
        }





        public async Task<List<CreditCardDto>> GetCreditDets(Client client)
        {
            List<CreditCard> cards = await _dbContext.CreditCards.Where(_ => _.ClientId == client.Id).ToListAsync();
            if (cards != null)
            {
                List<CreditCardDto> cardsdto = _mapper.Map<List<CreditCardDto>>(cards);
                return cardsdto;
            }
            else
            {
                Console.WriteLine("This client has no credit cards");
                return null;
            }


        }

        public async Task<List<CertificateDto>> GetCertificateDets(Client client)
        {
            List<Certificate> cert = await _dbContext.Certificates.Where(_ => _.ClientId == client.Id).ToListAsync();
            if (cert != null)
            {
                List<CertificateDto> certsdto = _mapper.Map<List<CertificateDto>>(cert);
                return certsdto;
            }
            else
            {
                Console.WriteLine("This client has no certificates");
                return null;
            }


        }

        public async Task<ActionResult<Transfer>> transferMoney(double amount, int sender_id, int receiver_id)
        {
            if(amount <= 0||sender_id == 0||receiver_id == 0) 
            {
                return null;
            }
            else
            {
                var sender = await _dbContext.Clients.FindAsync(sender_id);
                var reciever = await _dbContext.Clients.FindAsync(receiver_id);
                if(sender.balance < amount)
                {
                    return null;
                }
                else
                {
                    sender.balance -= amount;
                    reciever.balance += amount;
                    Transfer t1= new Transfer();
                    t1.Amount = amount;
                    t1.SenderId = sender_id;
                    t1.RecieverId = receiver_id;
                    t1.Date = DateTime.Now;
                    _dbContext.Transfers.Add(t1);
                    _dbContext.Entry(sender).State = EntityState.Modified;
                    _dbContext.Entry(reciever).State = EntityState.Modified;
                    await _dbContext.SaveChangesAsync();
                    return t1;
                }
                
            }
        }

        public async Task<ActionResult<(Certificate, string)>> add_certificate(int employee_id,Certificate c1)
        {
            var client = await _dbContext.Clients.FindAsync(employee_id);
            if(client == null||c1==null)
            {
                return (null, "sorry this user id is invalid ");
            }
            else
            {
                c1.Accepted = false;
                client.ClientCertificates.Add(c1);
                _dbContext.Entry(client).State = EntityState.Modified;
                await _dbContext.Certificates.AddAsync(c1);
                await _dbContext.SaveChangesAsync();
                return (c1, " certificate application accepted and waiting for the response");
            }  
        }
        public async Task<ActionResult<(CreditCard, string)>> addCreditCard(int ClientId, CreditCard creditCard)
        {
            var client = await _dbContext.Clients.FindAsync(ClientId);
            if (client == null || creditCard == null)
            {
                return (null, "sorry this user id is invalid ");
            }
            else
            {
                creditCard.Accepted = false;
                client.ClientCreditCards.Add(creditCard);
                _dbContext.Entry(client).State = EntityState.Modified;
                await _dbContext.CreditCards.AddAsync(creditCard);
                await _dbContext.SaveChangesAsync();
                return (creditCard, "Credit Card application is pending and waiting for the response");
            }
        }
        public double GenerateBalance()
        {
            Random random = new Random();
            double balance = random.Next(10000, 10000000);
            return balance;
        }

        public async Task<string> ReadTextAsync(string filePath)
        {
            using (StreamReader reader = new StreamReader(filePath))
            {
                return await reader.ReadToEndAsync();
            }
        }
        public async Task<string> CreateAsync(string name, IDictionary<string, string> dictionary)
        {
            Console.WriteLine("CreateAsync from TemplateService is called for the template " + name);
            string directoryName = Directory.GetCurrentDirectory();
            string text = await ReadTextAsync(directoryName + Path.DirectorySeparatorChar + "HtmlPreviews" + Path.DirectorySeparatorChar + name);
            
            foreach (KeyValuePair<string, string> item in dictionary)
            {
                text = text.Replace("{{" + item.Key + "}}", item.Value);
            }

            return text;
        }
        public async Task<byte[]> GetTransferPDF(int transaction_id)
        {
           var transaction= await _dbContext.Transfers.FindAsync(transaction_id);
            if (transaction != null)
            {
                // getting transaction infos and making the dictionary to  file the html placeholders
                var sender= await _dbContext.Clients.FindAsync(transaction.SenderId);
                var receiver = await _dbContext.Clients.FindAsync(transaction.RecieverId);
                var data = new Dictionary<string, string>();
                data.Add("SenderName", sender.FirstName+" "+sender.LastName);
                data.Add("ReceiverName", receiver.FirstName + " " + receiver.LastName);
                data.Add("Amount", transaction.Amount.ToString());
                data.Add("TransactionID", transaction_id.ToString());
                data.Add("Date", transaction.Date.ToString());
                
                Console.WriteLine("HTMLToPDF function from QRCodeService is called to generate a ticket from the template " + "Transactionpage");
                HtmlToPdf converter = new HtmlToPdf();
                converter.Options.PdfPageSize = PdfPageSize.Custom;
                converter.Options.PdfPageCustomSize = new SizeF(400f, 400f);
                string htmlString = await CreateAsync("Transactionpage.html", data); 
                Console.WriteLine(htmlString);  
                string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string baseUrl = directoryName + Path.DirectorySeparatorChar + "Templates" + Path.DirectorySeparatorChar + "imgs";
                PdfDocument pdfDocument = converter.ConvertHtmlString(htmlString, baseUrl);
                MemoryStream memoryStream = new MemoryStream();
                byte[] result = pdfDocument.Save();
                pdfDocument.Close();
                memoryStream.Position = 0L;

                return result;

            }
            else
            {
                return null;
            }
            
        }
        public async Task<byte[]> GetcreditCard(int creditcard_id)
        {
            var creditcard = await _dbContext.CreditCards.FindAsync(creditcard_id);
            if (creditcard != null)
            {
                // getting transaction infos and making the dictionary to  file the html placeholders
                
                var client = await _dbContext.Clients.FindAsync(creditcard.ClientId);
                var data = new Dictionary<string, string>();
                data.Add("Name", client.FirstName + " " + client.LastName);
                data.Add("Balance", client.balance.ToString());
                data.Add("CreditCardNumber",creditcard.CardNumber);
                data.Add("expirydate", creditcard.ExpiryDate.ToString());
                data.Add("CreditCardType", creditcard.CardType);
                data.Add("CVV", creditcard.CVV);

                Console.WriteLine("HTMLToPDF function from QRCodeService is called to generate a ticket from the template " + "Transactionpage");
                HtmlToPdf converter = new HtmlToPdf();
                converter.Options.PdfPageSize = PdfPageSize.Custom;
                converter.Options.PdfPageCustomSize = new SizeF(400f, 400f);
                string htmlString = await CreateAsync("CreditCardPage.html", data);
                Console.WriteLine(htmlString);
                string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string baseUrl = directoryName + Path.DirectorySeparatorChar + "Templates" + Path.DirectorySeparatorChar + "imgs";
                PdfDocument pdfDocument = converter.ConvertHtmlString(htmlString, baseUrl);
                MemoryStream memoryStream = new MemoryStream();
                byte[] result = pdfDocument.Save();
                pdfDocument.Close();
                memoryStream.Position = 0L;

                return result;

            }
            else
            {
                return null;
            }

        }
        public async Task<byte[]> GetCertificatePdf(int certificate_id)
        {
            var certificate = await _dbContext.Certificates.FindAsync(certificate_id);
            if (certificate != null)
            {
                // getting transaction infos and making the dictionary to  file the html placeholders

                var client = await _dbContext.Clients.FindAsync(certificate.ClientId);
                var data = new Dictionary<string, string>();
                data.Add("Name", client.FirstName + " " + client.LastName);
                data.Add("Balance", client.balance.ToString());
                data.Add("CertificateType", certificate.CertificateType.ToString());
                data.Add("CertificateId", certificate.Id.ToString());
                data.Add("CertificateIntrest", certificate.InterestPercentage.ToString()+" % ");

                Console.WriteLine("HTMLToPDF function from QRCodeService is called to generate a ticket from the template " + "Transactionpage");
                HtmlToPdf converter = new HtmlToPdf();
                converter.Options.PdfPageSize = PdfPageSize.Custom;
                converter.Options.PdfPageCustomSize = new SizeF(400f, 400f);
                string htmlString = await CreateAsync("CertificatePage.html", data);
                Console.WriteLine(htmlString);
                string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string baseUrl = directoryName + Path.DirectorySeparatorChar + "Templates" + Path.DirectorySeparatorChar + "imgs";
                PdfDocument pdfDocument = converter.ConvertHtmlString(htmlString, baseUrl);
                MemoryStream memoryStream = new MemoryStream();
                byte[] result = pdfDocument.Save();
                pdfDocument.Close();
                memoryStream.Position = 0L;

                return result;

            }
            else
            {
                return null;
            }

        }
    }
}
