using System;

namespace SocketHttpListener.Net
{
  /// <summary>
  /// Provides the credentials for HTTP authentication (Basic/Digest).
  /// </summary>
  public class NetworkCredential
  {
    #region Private Fields

    private string    _domain;
    private string    _password;
    private string [] _roles;
    private string    _username;

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkCredential"/> class
    /// with the specified user name and password.
    /// </summary>
    /// <param name="username">
    /// A <see cref="string"/> that represents the user name associated with the
    /// credentials.
    /// </param>
    /// <param name="password">
    /// A <see cref="string"/> that represents the password for the user name
    /// associated with the credentials.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="username"/> is <see langword="null"/> or empty.
    /// </exception>
    public NetworkCredential (string username, string password)
      : this (username, password, null, new string [0])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkCredential"/> class
    /// with the specified user name, password, domain, and roles.
    /// </summary>
    /// <param name="username">
    /// A <see cref="string"/> that represents the user name associated with the
    /// credentials.
    /// </param>
    /// <param name="password">
    /// A <see cref="string"/> that represents the password for the user name
    /// associated with the credentials.
    /// </param>
    /// <param name="domain">
    /// A <see cref="string"/> that represents the name of the user domain
    /// associated with the credentials.
    /// </param>
    /// <param name="roles">
    /// An array of <see cref="string"/> that contains the role names to which
    /// the user associated with the credentials belongs if any.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="username"/> is <see langword="null"/> or empty.
    /// </exception>
    public NetworkCredential (
      string username, string password, string domain, params string [] roles)
    {
      if (username == null || username.Length == 0)
        throw new ArgumentException ("Must not be null or empty.", "username");

      _username = username;
      _password = password;
      _domain = domain;
      _roles = roles;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the name of the user domain associated with the credentials.
    /// </summary>
    /// <value>
    /// A <see cref="string"/> that represents the name of the user domain
    /// associated with the credentials.
    /// </value>
    public string Domain {
      get {
        return _domain ?? String.Empty;
      }

      internal set {
        _domain = value;
      }
    }

    /// <summary>
    /// Gets the password for the user name associated with the credentials.
    /// </summary>
    /// <value>
    /// A <see cref="string"/> that represents the password for the user name
    /// associated with the credentials.
    /// </value>
    public string Password {
      get {
        return _password ?? String.Empty;
      }

      internal set {
        _password = value;
      }
    }

    /// <summary>
    /// Gets the role names to which the user associated with the credentials
    /// belongs.
    /// </summary>
    /// <value>
    /// An array of <see cref="string"/> that contains the role names to which
    /// the user associated with the credentials belongs.
    /// </value>
    public string [] Roles {
      get {
        return _roles;
      }

      internal set {
        _roles = value;
      }
    }

    /// <summary>
    /// Gets the user name associated with the credentials.
    /// </summary>
    /// <value>
    /// A <see cref="string"/> that represents the user name associated with the
    /// credentials.
    /// </value>
    public string UserName {
      get {
        return _username;
      }

      internal set {
        _username = value;
      }
    }

    #endregion
  }
}
