import axios from "axios";
import { useState, type SyntheticEvent  } from "react";



function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const handleSubmit = async (e: SyntheticEvent) => {
    e.preventDefault();

    const response = await axios.post(
        "http://localhost:5244/api/Auth/login",
        {
            email,
            password
        }
    );
    
    localStorage.setItem("token", response.data.token);

    await testEndPoint();

    console.log("Login successful");
  }

  const testEndPoint = async () => {

    const token = localStorage.getItem("token");

    const response = await axios.get(
        "http://localhost:5244/api/Posts",
        {
            headers: {
                Authorization: `Bearer ${token}`
            }
        }
    );

    console.log(response);
  }

  return (
    <div>
      <h1>Login</h1>
        <form onSubmit={handleSubmit}>
            <input
                type="email"
                placeholder="Email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
            />

            <input
                type="password"
                placeholder="Password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
            />

            <button type="submit">Login</button>
        </form>
    </div>
  );
}

export default LoginPage;