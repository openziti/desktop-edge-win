package cmd

import (
	"fmt"
	"github.com/spf13/cobra"
	"os"
	"path/filepath"
)
var exeName, _ = os.Executable()
var rootCmd = &cobra.Command{
	Use:  filepath.Base(exeName) ,
	Short: "Runs the ziti-tunnel as a service",
	Long: `This program provides access to a ziti-based network. 
           When executed without parameters it is expected that this application is running as a service`,
	Run: func(cmd *cobra.Command, args []string) {
		// Do Stuff Here
		fmt.Println("i am in command")
	},
}

func Execute() {
	if err := rootCmd.Execute(); err != nil {
		fmt.Println(err)
		os.Exit(1)
	}

}
